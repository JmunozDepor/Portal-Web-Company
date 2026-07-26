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
    string? Status = null);

public sealed record InventoryDocumentLineDto(
    string ItemCode,
    string? Description,
    decimal Quantity,
    string FromWarehouseCode,
    string ToWarehouseCode);

public sealed record InventoryDocumentFilter(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    int? DocNum = null);

public sealed record InventoryDocumentListResult(IReadOnlyList<InventoryDocumentSummaryDto> Items, int TotalRecords);

/// <summary>
/// Fila de listado -- non-positional, se mapea vía IHanaService.QueryAsync (ver
/// CustomerDto). Sin almacén origen/destino acá a propósito: es un dato por línea, no
/// de cabecera (ver el doc-comment de InventoryDocumentDto) -- mostrarlo en el listado
/// exigiría un JOIN contra WTQ1/WTR1 que no se justifica todavía. El detalle del
/// documento sí los muestra, línea por línea.
/// </summary>
public sealed record InventoryDocumentSummaryDto
{
    public int DocEntry { get; init; }
    public int DocNum { get; init; }
    public DateTime DocDate { get; init; }
    public string Status { get; init; } = null!;
}
