namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Documento de venta genérico (SAP ORDR/ORIN/OINV/ORRR/ORDN, ver SalesDocumentType) --
/// digitación directa, generaliza el antiguo SalesOrderDto (una Orden de Venta es hoy
/// solo el primer valor de SalesDocumentType, ver CLAUDE.md). Construido/mapeado a mano
/// por ISalesDocumentService, nunca vía IHanaService.QueryAsync -- puede ser un record
/// posicional normal (a diferencia de los DTOs de catálogo, que sí pasan por reflection,
/// ver CustomerDto).
/// </summary>
public sealed record SalesDocumentDto(
    string CustomerCardCode,
    string? CustomerName,
    int? SalesEmployeeCode,
    string? Comments,
    DateOnly DocDate,
    DateOnly DocDueDate,
    DateOnly TaxDate,
    string? CustomerReferenceNumber,
    IReadOnlyList<SalesDocumentLineDto> Lines,
    int? DocEntry = null,
    int? DocNum = null,
    decimal? DocTotal = null,
    string? Status = null,
    // NNM1.Series -- null deja que SAP asigne la serie por defecto del tipo de documento. Ver ISeriesCatalogService.
    int? Series = null,
    // Tab Logística -- solo Venta (Compras/Inventario no la tienen a propósito, ver CLAUDE.md).
    int? ShippingMethodCode = null,
    // Tab Finanzas -- solo Venta (idem).
    int? PaymentTermsGroupCode = null,
    // Campos de usuario (UDF dinámicos) -- consumido por Modulo.ImportacionGenerica, ver
    // SapAdditionalFieldsHelper. Null = sin campos adicionales, payload idéntico a antes.
    IReadOnlyDictionary<string, object?>? AdditionalFields = null);

/// <summary>
/// Línea de Artículo o Servicio -- SAP no permite mezclar los dos tipos en el mismo
/// documento (ver SalesDocumentService.ResolveDocType), así que Type debe ser el mismo
/// en todas las líneas de una misma llamada a CreateAsync. Artículo usa
/// ItemCode/WarehouseCode; Servicio usa Description/AccountCode/CostCenterCode -- los
/// campos que no aplican al tipo quedan null, nunca se postean a SAP (ver
/// SalesDocumentService.CreateAsync).
/// </summary>
public sealed record SalesDocumentLineDto(
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
    // Venta, ver CLAUDE.md "Paridad entre los motores genéricos de documento".
    int? BaseType = null,
    int? BaseEntry = null,
    int? BaseLine = null,
    // Campos de usuario de línea -- ver SalesDocumentDto.AdditionalFields.
    IReadOnlyDictionary<string, object?>? AdditionalFields = null);

/// <summary>
/// Paridad con FiltroGenericoVenta del original (referencia-original/PortalSAP_v2) --
/// mismos 7 campos (CustomerCardCode/CustomerName/CustomerReferenceNumber/DocNum/
/// DateFrom/DateTo/SalesEmployeeName), todos LIKE parcial salvo las fechas (ver
/// SalesDocumentService.BuildWhereClause). DocNum es string, no int -- el original
/// permite buscar "123" como substring de "51230", no solo número exacto.
/// </summary>
public sealed record SalesDocumentFilter(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    string? CustomerCardCode = null,
    string? CustomerName = null,
    string? CustomerReferenceNumber = null,
    string? SalesEmployeeName = null,
    string? DocNum = null);

public sealed record SalesDocumentListResult(IReadOnlyList<SalesDocumentSummaryDto> Items, int TotalRecords);

/// <summary>Fila de listado -- non-positional, se mapea vía IHanaService.QueryAsync (ver CustomerDto).</summary>
public sealed record SalesDocumentSummaryDto
{
    public int DocEntry { get; init; }
    public int DocNum { get; init; }
    public string CustomerCardCode { get; init; } = null!;
    public string? CustomerName { get; init; }
    /// <summary>SAP "Address2" (dirección de despacho de la cabecera) -- "Sucursal entrega" en el original.</summary>
    public string? DeliveryAddress { get; init; }
    public DateTime DocDate { get; init; }
    public decimal DocTotal { get; init; }
    public string? CustomerReferenceNumber { get; init; }
    public string? SalesEmployeeName { get; init; }
    public string Status { get; init; } = null!;
}
