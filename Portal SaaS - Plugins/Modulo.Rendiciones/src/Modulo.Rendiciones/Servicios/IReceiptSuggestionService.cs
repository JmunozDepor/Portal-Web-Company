namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Toma lo que leyó el OCR (<see cref="ExtractedReceiptDto"/>) y arma la PROPUESTA final
/// para precargar el formulario del gasto: normaliza el RUT, deriva neto/IVA cuando el
/// comprobante no los desglosa, mapea el tipo de documento al catálogo de la compañía,
/// y consulta la memoria del proveedor (<see cref="ISupplierHintService"/>) para
/// proponer la categoría (pre-seleccionada si hay confianza, o solo sugerida).
/// </summary>
public interface IReceiptSuggestionService
{
    Task<ReceiptProposal> BuildAsync(Guid companyId, ExtractedReceiptDto ocr, CancellationToken ct = default);
}

/// <summary>
/// Valores propuestos para una línea de gasto. <see cref="ExpenseTypeId"/> viene
/// pre-seleccionado solo cuando la memoria del proveedor tiene >= 2 confirmaciones;
/// <see cref="SuggestedExpenseTypeId"/> es una sugerencia (del modelo o de una memoria
/// aún poco confiable) que el usuario acepta con un clic. <see cref="Error"/> se
/// propaga tal cual desde el OCR.
/// </summary>
public sealed record ReceiptProposal(
    decimal? Amount,
    decimal? TaxAmount,
    decimal? NetAmount,
    decimal? ExemptAmount,
    DateTime? Date,
    TimeOnly? TransactionTime,
    string? DocumentNumber,
    string? SupplierTaxId,
    string? SupplierName,
    long? DocumentTypeId,
    long? ExpenseTypeId,
    long? SuggestedExpenseTypeId,
    string CaptureSource,
    string? Error)
{
    public static ReceiptProposal FromError(string? error) =>
        new(null, null, null, null, null, null, null, null, null, null, null, null, "document", error);
}
