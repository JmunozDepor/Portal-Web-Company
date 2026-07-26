namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Documento de inventario genérico (SAP OWTQ/OWTR, ver InventoryDocumentType) --
/// digitación directa, traslado de mercadería entre 2 almacenes. FromWarehouseCode/
/// ToWarehouseCode son el DEFAULT a nivel de documento -- se copian a cada línea al
/// crear (SAP exige WarehouseCode/FromWarehouseCode por línea en el wire real, ver
/// SapInventoryDocumentModels), pero el formulario solo pide el par de almacenes una
/// vez para no complicar la digitación (mismo criterio de simplicidad que Venta/Compra
/// en esta primera entrega).
/// </summary>
public sealed record InventoryDocumentDto(
    string FromWarehouseCode,
    string ToWarehouseCode,
    DateOnly DocDate,
    DateOnly DocDueDate,
    string? Comments,
    IReadOnlyList<InventoryDocumentLineDto> Lines,
    int? DocEntry = null,
    int? DocNum = null,
    string? Status = null);

public sealed record InventoryDocumentLineDto(
    string ItemCode,
    string? Description,
    decimal Quantity);

public sealed record InventoryDocumentFilter(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    int? DocNum = null);

public sealed record InventoryDocumentListResult(IReadOnlyList<InventoryDocumentSummaryDto> Items, int TotalRecords);

/// <summary>
/// Fila de listado -- non-positional, se mapea vía IHanaService.QueryAsync (ver
/// CustomerDto). Sin almacén origen/destino acá a propósito: los nombres de columna
/// físicos de OWTQ/OWTR para eso no están confirmados contra un ambiente real todavía
/// (a diferencia de los nombres de Service Layer -- "FromWarehouse"/"ToWarehouse" --
/// que sí se usan en GetAsync/CreateAsync). El detalle del documento sí los muestra.
/// </summary>
public sealed record InventoryDocumentSummaryDto
{
    public int DocEntry { get; init; }
    public int DocNum { get; init; }
    public DateTime DocDate { get; init; }
    public string Status { get; init; } = null!;
}
