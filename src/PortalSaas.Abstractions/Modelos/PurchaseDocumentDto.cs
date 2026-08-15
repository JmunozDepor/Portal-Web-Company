namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Documento de compra genérico (SAP OPQT/OPOR, ver PurchaseDocumentType) --
/// digitación directa, sin Copy-From ni aprobación en esta entrega (a diferencia del
/// original, donde Pedido nace de Copy-From al aprobar una Oferta -- acá los 2 tipos se
/// crean directo, mismo criterio "digitación directa" que Venta/Inventario).
/// </summary>
public sealed record PurchaseDocumentDto(
    string SupplierCardCode,
    string? SupplierName,
    string? Comments,
    DateOnly DocDate,
    DateOnly DocDueDate,
    DateOnly TaxDate,
    string? SupplierReferenceNumber,
    IReadOnlyList<PurchaseDocumentLineDto> Lines,
    int? DocEntry = null,
    int? DocNum = null,
    decimal? DocTotal = null,
    string? Status = null,
    // NNM1.Series -- null deja que SAP asigne la serie por defecto del tipo de documento. Ver ISeriesCatalogService.
    int? Series = null,
    // Campos de usuario (UDF dinámicos) -- consumido por Modulo.ImportacionGenerica, ver
    // SapAdditionalFieldsHelper.
    IReadOnlyDictionary<string, object?>? AdditionalFields = null);

/// <summary>
/// Línea de Artículo o Servicio -- mismo criterio que SalesDocumentLineDto (ver ese
/// doc-comment): SAP no permite mezclar los dos tipos en el mismo documento.
/// </summary>
public sealed record PurchaseDocumentLineDto(
    DocumentLineType Type,
    string? ItemCode,
    string? Description,
    decimal Quantity,
    decimal? UnitPrice,
    decimal DiscountPercent,
    string? WarehouseCode,
    string? AccountCode = null,
    string? CostCenterCode = null,
    // Dimensión2 (Marca, DimCode=2) / Dimensión3 (Tipo de Gasto, DimCode=5) -- solo
    // Servicio, opcionales (a diferencia de CostCenterCode/Dimensión1). Ver
    // ICostCenterCatalogService.ListByDimensionAsync.
    string? CostCenterCode2 = null,
    string? CostCenterCode3 = null,
    // Copy-From (paridad con InventoryDocumentLineDto) -- sin consumidor todavía en
    // Compra, ver CLAUDE.md "Paridad entre los motores genéricos de documento".
    int? BaseType = null,
    int? BaseEntry = null,
    int? BaseLine = null,
    // Campos de usuario de línea -- ver PurchaseDocumentDto.AdditionalFields.
    IReadOnlyDictionary<string, object?>? AdditionalFields = null);

/// <summary>
/// Paridad con FiltroGenericoCompra del original (referencia-original/PortalSAP_v2) --
/// mismos 5 campos (SupplierCardCode/SupplierName/SupplierReferenceNumber/DocNum/
/// DateFrom/DateTo), todos LIKE parcial salvo las fechas (ver
/// PurchaseDocumentService.BuildWhereClause). DocNum es string, no int -- mismo criterio
/// que SalesDocumentFilter (regla de paridad entre motores, ver CLAUDE.md).
/// </summary>
public sealed record PurchaseDocumentFilter(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    string? SupplierCardCode = null,
    string? SupplierName = null,
    string? SupplierReferenceNumber = null,
    string? DocNum = null);

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
