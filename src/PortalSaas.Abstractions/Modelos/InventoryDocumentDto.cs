namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Documento de inventario genérico (SAP OWTQ/OWTR, ver InventoryDocumentType) --
/// digitación directa, traslado de mercadería entre almacenes. Almacén origen/destino
/// es POR LÍNEA (ver InventoryDocumentLineDto), no de cabecera -- confirmado contra
/// referencia-original/PortalSAP_v2 (CLAUDE.md, "Modulo.Inventario"): OWTQ/OWTR no
/// tienen columna de almacén de origen a nivel de cabecera (solo WTQ1/WTR1), y el
/// almacén destino de cabecera se sacó del formulario a pedido del cliente porque
/// igual se vuelve a digitar por línea. Sin DocDueDate a propósito -- Service Layer la
/// rechaza para StockTransfer ("Property 'DocDueDate' of 'StockTransfer' is invalid"),
/// mismo motivo documentado en el original.
/// </summary>
public sealed record InventoryDocumentDto(
    DateOnly DocDate,
    string? Comments,
    IReadOnlyList<InventoryDocumentLineDto> Lines,
    int? DocEntry = null,
    int? DocNum = null,
    string? Status = null,
    // NNM1.Series -- null deja que SAP asigne la serie por defecto del tipo de documento. Ver ISeriesCatalogService.
    int? Series = null,
    // Campos de usuario (UDF dinámicos) -- consumido por Modulo.ImportacionGenerica, ver
    // SapAdditionalFieldsHelper.
    IReadOnlyDictionary<string, object?>? AdditionalFields = null);

public sealed record InventoryDocumentLineDto(
    string ItemCode,
    string? Description,
    decimal Quantity,
    string FromWarehouseCode,
    string ToWarehouseCode,
    // Campos de usuario de línea -- ver InventoryDocumentDto.AdditionalFields.
    IReadOnlyDictionary<string, object?>? AdditionalFields = null);

/// <summary>
/// DocNum es string, no int -- mismo criterio que SalesDocumentFilter/
/// PurchaseDocumentFilter (regla de paridad entre motores, ver CLAUDE.md): buscar "123"
/// encuentra "51230", no solo número exacto (ver InventoryDocumentService.BuildWhereClause).
/// BusinessPartnerCardCode/BusinessPartnerName/WarehouseDestinationCode -- paridad con
/// FiltroGenericoInventario del original: aunque OWTQ/OWTR no tienen socio de negocio ni
/// almacén destino EDITABLES desde el formulario (ver el doc-comment de
/// InventoryDocumentDto), esas columnas SÍ existen en la cabecera SAP (CardCode/CardName/
/// ToWhsCode) -- un documento creado directo en SAP, o importado, puede traerlas
/// pobladas, y el listado debe poder filtrar/mostrarlas igual que el original.
/// </summary>
public sealed record InventoryDocumentFilter(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    string? DocNum = null,
    string? BusinessPartnerCardCode = null,
    string? BusinessPartnerName = null,
    string? WarehouseDestinationCode = null);

public sealed record InventoryDocumentListResult(IReadOnlyList<InventoryDocumentSummaryDto> Items, int TotalRecords);

/// <summary>
/// Fila de listado -- non-positional, se mapea vía IHanaService.QueryAsync (ver
/// CustomerDto). Paridad con GenericoInventarioResumenDto del original: socio de
/// negocios/nombre/dirección de destino ("Sucursal entrega", Address2)/almacén destino
/// de cabecera (ToWhsCode, TO_VARCHAR igual que WarehouseCatalogService) y
/// CustomerReferenceNumber ("N.° ref.", UDF U_NumAtCard -- OWTQ/OWTR no traen el
/// NumAtCard estándar de los documentos de marketing, mismo hallazgo ya documentado
/// del original). Sin almacén origen/destino DE LÍNEA acá a propósito: es un dato por
/// línea, no de cabecera -- mostrarlo en el listado exigiría un JOIN contra WTQ1/WTR1
/// que no se justifica todavía; el detalle del documento sí lo muestra, línea por línea.
/// </summary>
public sealed record InventoryDocumentSummaryDto
{
    public int DocEntry { get; init; }
    public int DocNum { get; init; }
    public string? BusinessPartnerCardCode { get; init; }
    public string? BusinessPartnerName { get; init; }
    public DateTime DocDate { get; init; }
    public string? DeliveryAddress { get; init; }
    public string? CustomerReferenceNumber { get; init; }
    public string? WarehouseDestinationCode { get; init; }
    public string Status { get; init; } = null!;
}
