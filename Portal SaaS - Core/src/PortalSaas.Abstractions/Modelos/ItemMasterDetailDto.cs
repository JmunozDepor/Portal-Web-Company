namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Ficha de artículo (OITM + OITB) de la compañía SAP activa -- non-positional, ver
/// el doc-comment de ItemDto (mismo motivo: mapeo por reflection de
/// IHanaService.QueryAsync). A diferencia de ItemDto (usado por la búsqueda en vivo),
/// este DTO trae los campos completos de la ficha del visor Maestro de Producto.
/// </summary>
public sealed record ItemMasterDetailDto
{
    public string ItemCode { get; init; } = null!;
    public string ItemName { get; init; } = null!;
    public string? CodeBars { get; init; }
    public string? ItemGroupCode { get; init; }
    public string? ItemGroupName { get; init; }
}
