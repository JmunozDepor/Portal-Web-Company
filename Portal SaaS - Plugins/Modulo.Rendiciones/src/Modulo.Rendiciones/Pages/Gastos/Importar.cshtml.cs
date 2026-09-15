using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Gastos;

/// <summary>
/// Import masivo con OCR: hasta 5 PDF o una foto por vez, vía Azure Document
/// Intelligence (IReceiptExtractorService). Cada archivo se guarda como comprobante y
/// se crea un gasto Loose SIN categoría todavía (ExpenseReportLine.ExpenseTypeId
/// nullable a propósito) -- el usuario la completa al revisar en Pages/Gastos/Detalle,
/// uno por uno. No hay un wizard con estado en memoria entre pasos: cada archivo queda
/// persistido de una, y "revisar" es simplemente editar el gasto recién creado como
/// cualquier otro.
/// </summary>
public sealed class ImportarModel : RendicionesRendidorPageModelBase
{
    private const int MaxFiles = 5;

    private readonly IReceiptExtractorService _extractor;
    private readonly IReceiptSuggestionService _suggestions;
    private readonly IExpenseTypeService _expenseTypes;
    private readonly IAttachmentStorageService _attachments;
    private readonly IExpenseService _expenses;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ILogger<ImportarModel> _logger;

    public ImportarModel(IReceiptExtractorService extractor, IReceiptSuggestionService suggestions,
        IExpenseTypeService expenseTypes, IAttachmentStorageService attachments, IExpenseService expenses,
        IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany,
        ILogger<ImportarModel> logger)
        : base(roles, currentUser, currentCompany)
    {
        _extractor = extractor;
        _suggestions = suggestions;
        _expenseTypes = expenseTypes;
        _attachments = attachments;
        _expenses = expenses;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
        _logger = logger;
    }

    [BindProperty]
    public List<IFormFile> Archivos { get; set; } = new();

    public List<ImportResult> Results { get; private set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Archivos.Count == 0)
        {
            ErrorMessage = "Elegí al menos un archivo (PDF o foto).";
            return Page();
        }

        if (Archivos.Count > MaxFiles)
        {
            ErrorMessage = $"Máximo {MaxFiles} archivos por vez -- subí el resto en una segunda tanda.";
            return Page();
        }

        var tipos = await _expenseTypes.ListActiveAsync(_currentCompany.CompanyId, ct);

        foreach (var file in Archivos)
        {
            if (file.Length == 0)
                continue;

            try
            {
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream, ct);
                var content = stream.ToArray();

                var extracted = await _extractor.ExtractAsync(_currentCompany.CompanyId, content, file.ContentType, ct);
                var p = await _suggestions.BuildAsync(_currentCompany.CompanyId, extracted, ct);

                var receiptId = await _attachments.SaveAsync(_currentCompany.CompanyId, _currentUser.UserId,
                    file.FileName, file.ContentType, content, ct);

                // La categoría pre-seleccionada (memoria confiable del proveedor) se
                // persiste directo; si dispara una política BLOQUEANTE, se reintenta sin
                // categoría para no perder la importación -- queda para revisar.
                // El OCR devuelve texto libre no confiable: recortarlo a los largos de la
                // tabla antes de insertar (un RUT mal leído de 25 chars revienta el SaveChanges).
                ExpenseReportLine Build(long? expenseTypeId) => new()
                {
                    CompanyId = _currentCompany.CompanyId,
                    UserId = _currentUser.UserId,
                    ExpenseTypeId = expenseTypeId,
                    DocumentTypeId = p.DocumentTypeId,
                    Date = p.Date ?? DateTime.Today,
                    TransactionTime = p.TransactionTime,
                    Amount = SafeAmount(p.Amount),
                    TaxAmount = SafeMoney(p.TaxAmount),
                    NetAmount = SafeMoney(p.NetAmount),
                    ExemptAmount = SafeMoney(p.ExemptAmount),
                    Currency = "CLP",
                    DocumentNumber = Clamp(p.DocumentNumber, 50),
                    SupplierTaxId = Clamp(p.SupplierTaxId, 20),
                    SupplierName = Clamp(p.SupplierName, 200),
                    CaptureSource = p.CaptureSource,
                    ExpenseReceiptId = receiptId,
                };

                ExpenseSavedResult saved;
                try
                {
                    saved = await _expenses.CreateAsync(Build(p.ExpenseTypeId), ct);
                }
                catch (InvalidOperationException) when (p.ExpenseTypeId is not null)
                {
                    saved = await _expenses.CreateAsync(Build(null), ct);
                }

                var sugeridaId = p.ExpenseTypeId ?? p.SuggestedExpenseTypeId;
                var sugeridaNombre = sugeridaId is { } sid ? tipos.FirstOrDefault(t => t.Id == sid)?.Name : null;
                Results.Add(new ImportResult(file.FileName, saved.Id, extracted, saved.Warnings,
                    p.ExpenseTypeId is not null, sugeridaNombre));
            }
            catch (Exception ex)
            {
                // Un archivo que falla (OCR caído, política que bloquea, error de
                // guardado) no debe tumbar toda la tanda con un 500 -- se registra y
                // se muestra como fila fallida; los demás archivos siguen.
                _logger.LogError(ex, "Falló la importación del comprobante {FileName} para la compañía {CompanyId}.",
                    file.FileName, _currentCompany.CompanyId);
                Results.Add(new ImportResult(file.FileName, 0,
                    new ExtractedReceiptDto(null, null, null, null, null, null, null,
                        Error: $"No se pudo importar este archivo: {DescribeError(ex)}"),
                    Array.Empty<string>(), false, null));
            }
        }

        var ok = Results.Count(r => r.ExpenseId > 0);
        var fallidos = Results.Count - ok;

        if (ok > 0)
        {
            SuccessMessage = ok == 1
                ? "Se importó 1 gasto -- revisalo y completá la categoría antes de agregarlo a un informe."
                : $"Se importaron {ok} gastos -- revisalos y completá la categoría de cada uno antes de agregarlos a un informe.";
        }

        if (fallidos > 0)
        {
            ErrorMessage = fallidos == 1
                ? "1 archivo no se pudo importar -- mirá el detalle en la lista de abajo."
                : $"{fallidos} archivos no se pudieron importar -- mirá el detalle en la lista de abajo.";
        }

        return Page();
    }

    /// <summary>Tope defensivo para montos venidos de OCR -- cabe en numeric(18,2) y descarta basura.</summary>
    private const decimal MaxAmount = 1_000_000_000_000m;

    private static decimal SafeAmount(decimal? amount) =>
        amount is { } a && a >= 0m && a < MaxAmount ? a : 0m;

    private static decimal? SafeMoney(decimal? amount) =>
        amount is { } a && a >= 0m && a < MaxAmount ? a : null;

    /// <summary>Recorta a <paramref name="max"/> caracteres (null/blank -&gt; null) -- el OCR no respeta longitudes.</summary>
    private static string? Clamp(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    /// <summary>
    /// Aplana la cadena de <see cref="Exception.InnerException"/> -- EF envuelve el error
    /// real (constraint violado, valor demasiado largo, columna inexistente) dentro de un
    /// "An error occurred while saving the entity changes"; sin esto el mensaje no dice nada.
    /// </summary>
    private static string DescribeError(Exception ex)
    {
        var mensajes = new List<string>();
        for (var actual = ex; actual is not null; actual = actual.InnerException)
        {
            var m = actual.Message.Trim();
            if (m.Length > 0 && (mensajes.Count == 0 || mensajes[^1] != m))
                mensajes.Add(m);
        }
        return string.Join(" → ", mensajes);
    }

    public sealed record ImportResult(
        string FileName, long ExpenseId, ExtractedReceiptDto Extracted, IReadOnlyList<string> Warnings,
        bool CategoryPreselected, string? SuggestedCategoryName);
}
