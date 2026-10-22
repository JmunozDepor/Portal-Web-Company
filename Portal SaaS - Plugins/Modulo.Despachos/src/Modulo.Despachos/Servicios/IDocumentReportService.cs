namespace Modulo.Despachos.Servicios;

/// <summary>
/// Filtros del reporte de documentos despachados. Fechas = dispatch_date del manifiesto (inclusive).
/// Search: folio exacto, N° de manifiesto o parte del nombre del cliente.
/// </summary>
public sealed record DocumentReportFilter(
    DateOnly From, DateOnly To, string? DeliveryStatus = null, long? CarrierId = null, string? Search = null);

/// <summary>Un documento del manifiesto con su condición de entrega (retorno).</summary>
public sealed record DocumentReportRow(
    Guid Id, string DocumentKind, int SiiDocumentType, long Folio, string CustomerName, string? Commune,
    decimal Units, int Packages, decimal Amount, string DeliveryStatus, string? RejectionReason, string? Comments,
    DateTime? ReturnedAt);

/// <summary>Resumen del manifiesto y sus documentos (solo los que pasan el filtro).</summary>
public sealed record DocumentReportManifest(
    Guid Id, int Number, DateOnly DispatchDate, string Status, string Carrier, string? Shift, string? Plate,
    string? Driver, DateTime? DepartedAt, DateTime? ReturnedAt, IReadOnlyList<DocumentReportRow> Documents)
{
    public decimal Units => Documents.Sum(d => d.Units);
    public int Packages => Documents.Sum(d => d.Packages);
    public decimal Amount => Documents.Sum(d => d.Amount);
}

public sealed record DocumentReport(IReadOnlyList<DocumentReportManifest> Manifests)
{
    public int TotalDocuments => Manifests.Sum(m => m.Documents.Count);
    public decimal TotalUnits => Manifests.Sum(m => m.Units);
    public int TotalPackages => Manifests.Sum(m => m.Packages);
    public decimal TotalAmount => Manifests.Sum(m => m.Amount);

    /// <summary>Cantidad de documentos por delivery_status.</summary>
    public IReadOnlyDictionary<string, int> CountByStatus =>
        Manifests.SelectMany(m => m.Documents).GroupBy(d => d.DeliveryStatus).ToDictionary(g => g.Key, g => g.Count());
}

/// <summary>
/// Reporte de documentos despachados y su estado de entrega, agrupado por manifiesto. Solo manifiestos
/// despachados, retornados o cerrados (sin borradores ni anulados).
/// </summary>
public interface IDocumentReportService
{
    /// <summary>Rango máximo de fechas permitido (días).</summary>
    const int MaxRangeDays = 366;

    Task<DocumentReport> GetAsync(DocumentReportFilter filter);

    /// <summary>Excel (ClosedXML): hoja "Documentos" (una fila por documento con los datos del manifiesto) y "Resumen".</summary>
    Task<byte[]> ExportExcelAsync(DocumentReportFilter filter);
}
