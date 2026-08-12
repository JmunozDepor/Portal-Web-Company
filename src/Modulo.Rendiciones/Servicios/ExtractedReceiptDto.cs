namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Lo que el OCR pudo leer de un comprobante -- todo nullable a propósito, el usuario
/// siempre revisa/corrige en el formulario antes de guardar. La categoría
/// (ExpenseTypeId) nunca sale de acá -- es una taxonomía propia del portal, no algo que
/// un modelo de OCR genérico pueda inferir.
/// </summary>
public sealed record ExtractedReceiptDto(
    decimal? Amount,
    decimal? TaxAmount,
    DateTime? Date,
    string? DocumentNumber,
    string? SupplierTaxId,
    string? SupplierName,
    float? Confidence,
    string? Error = null);
