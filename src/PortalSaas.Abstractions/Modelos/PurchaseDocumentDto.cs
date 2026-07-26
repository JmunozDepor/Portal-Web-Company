namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Documento de compra genérico (SAP OPQT/OPOR, ver PurchaseDocumentType) --
/// digitación directa, sin Copy-From ni aprobación en esta entrega (a diferencia del
/// original, donde Pedido nace de Copy-From al aprobar una Oferta -- acá los 2 tipos se
/// crean directo, mismo criterio "digitación directa" que Venta/Inventario). Solo
/// líneas de Artículo, mismo alcance recortado que el resto del proyecto.
/// </summary>
public sealed record PurchaseDocumentDto(
    string SupplierCardCode,
    string? SupplierName,
    string? Comments,
    DateOnly DocDate,
    DateOnly DocDueDate,
    string? SupplierReferenceNumber,
    IReadOnlyList<PurchaseDocumentLineDto> Lines,
    int? DocEntry = null,
    int? DocNum = null,
    decimal? DocTotal = null,
    string? Status = null);

public sealed record PurchaseDocumentLineDto(
    string ItemCode,
    string? Description,
    decimal Quantity,
    decimal? UnitPrice,
    decimal DiscountPercent,
    string WarehouseCode);

public sealed record PurchaseDocumentFilter(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    string? SupplierCardCode = null,
    int? DocNum = null);

public sealed record PurchaseDocumentListResult(IReadOnlyList<PurchaseDocumentSummaryDto> Items, int TotalRecords);

/// <summary>Fila de listado -- non-positional, se mapea vía IHanaService.QueryAsync (ver CustomerDto).</summary>
public sealed record PurchaseDocumentSummaryDto
{
    public int DocEntry { get; init; }
    public int DocNum { get; init; }
    public string SupplierCardCode { get; init; } = null!;
    public string? SupplierName { get; init; }
    public DateTime DocDate { get; init; }
    public decimal DocTotal { get; init; }
    public string? SupplierReferenceNumber { get; init; }
    public string Status { get; init; } = null!;
}
