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
    private readonly IAttachmentStorageService _attachments;
    private readonly IExpenseService _expenses;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ILogger<ImportarModel> _logger;

    public ImportarModel(IReceiptExtractorService extractor, IAttachmentStorageService attachments, IExpenseService expenses,
        IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany,
        ILogger<ImportarModel> logger)
        : base(roles, currentUser, currentCompany)
    {
        _extractor = extractor;
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

                var receiptId = await _attachments.SaveAsync(_currentCompany.CompanyId, _currentUser.UserId,
                    file.FileName, file.ContentType, content, ct);

                // ExpenseTypeId null: la política de tope no aplica todavía (no hay
                // categoría hasta que se revise), pero la detección de duplicados por
                // número de documento SÍ corre acá -- es justo el caso que más importa
                // (una foto/PDF importada dos veces por error).
                // El OCR devuelve texto libre no confiable: recortarlo a los límites de la
                // tabla antes de insertar (si no, un RUT mal leído de 25 caracteres o un
                // nombre larguísimo revientan el SaveChanges con "value too long").
                var saved = await _expenses.CreateAsync(new ExpenseReportLine
                {
                    CompanyId = _currentCompany.CompanyId,
                    UserId = _currentUser.UserId,
                    ExpenseTypeId = null,
                    Date = extracted.Date ?? DateTime.Today,
                    Amount = SafeAmount(extracted.Amount),
                    TaxAmount = extracted.TaxAmount is { } t && t >= 0m && t < MaxAmount ? t : null,
                    Currency = "CLP",
                    DocumentNumber = Clamp(extracted.DocumentNumber, 50),
                    SupplierTaxId = Clamp(extracted.SupplierTaxId, 20),
                    SupplierName = Clamp(extracted.SupplierName, 200),
                    ExpenseReceiptId = receiptId,
                }, ct);

                Results.Add(new ImportResult(file.FileName, saved.Id, extracted, saved.Warnings));
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
                    Array.Empty<string>()));
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

    public sealed record ImportResult(string FileName, long ExpenseId, ExtractedReceiptDto Extracted, IReadOnlyList<string> Warnings);
}
