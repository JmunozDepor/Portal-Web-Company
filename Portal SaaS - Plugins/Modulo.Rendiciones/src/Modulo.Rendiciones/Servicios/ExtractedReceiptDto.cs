namespace Modulo.Rendiciones.Servicios;

/// <summary>Qué motor produjo la lectura -- se propaga a ExpenseReportLine.CaptureSource.</summary>
public enum ReceiptSource
{
    None = 0,
    AzureOcr = 1,
    GeminiOcr = 2,
}

/// <summary>
/// Lo que el OCR pudo leer de un comprobante -- todo nullable a propósito, el usuario
/// siempre revisa/corrige en el formulario antes de guardar. La categoría de gasto no
/// se lee del documento (es taxonomía propia del portal); el modelo puede *sugerir* una
/// en <see cref="SuggestedCategoryText"/>, que IReceiptSuggestionService intenta mapear
/// a un ExpenseType real.
///
/// Campos posicionales nuevos van SIEMPRE al final con default -- hay call sites que
/// construyen el record posicionalmente.
/// </summary>
public sealed record ExtractedReceiptDto(
    decimal? Amount,
    decimal? TaxAmount,
    DateTime? Date,
    string? DocumentNumber,
    string? SupplierTaxId,
    string? SupplierName,
    float? Confidence,
    string? Error = null,
    TimeOnly? TransactionTime = null,
    decimal? NetAmount = null,
    decimal? ExemptAmount = null,
    int? SiiDocumentCode = null,
    IReadOnlyList<string>? Items = null,
    string? SuggestedCategoryText = null,
    ReceiptSource Source = ReceiptSource.None);
