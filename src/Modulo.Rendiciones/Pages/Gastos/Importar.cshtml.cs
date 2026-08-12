using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
public sealed class ImportarModel : RendicionesPageModelBase
{
    private const int MaxFiles = 5;

    private readonly IReceiptExtractorService _extractor;
    private readonly IAttachmentStorageService _attachments;
    private readonly IExpenseService _expenses;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public ImportarModel(IReceiptExtractorService extractor, IAttachmentStorageService attachments, IExpenseService expenses,
        ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
    {
        _extractor = extractor;
        _attachments = attachments;
        _expenses = expenses;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
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
            var saved = await _expenses.CreateAsync(new ExpenseReportLine
            {
                CompanyId = _currentCompany.CompanyId,
                UserId = _currentUser.UserId,
                ExpenseTypeId = null,
                Date = extracted.Date ?? DateTime.Today,
                Amount = extracted.Amount ?? 0m,
                TaxAmount = extracted.TaxAmount,
                Currency = "CLP",
                DocumentNumber = extracted.DocumentNumber,
                SupplierTaxId = extracted.SupplierTaxId,
                SupplierName = extracted.SupplierName,
                ExpenseReceiptId = receiptId,
            }, ct);

            Results.Add(new ImportResult(file.FileName, saved.Id, extracted, saved.Warnings));
        }

        SuccessMessage = Results.Count == 1
            ? "Se importó 1 gasto -- revisalo y completá la categoría antes de agregarlo a un informe."
            : $"Se importaron {Results.Count} gastos -- revisalos y completá la categoría de cada uno antes de agregarlos a un informe.";

        return Page();
    }

    public sealed record ImportResult(string FileName, long ExpenseId, ExtractedReceiptDto Extracted, IReadOnlyList<string> Warnings);
}
