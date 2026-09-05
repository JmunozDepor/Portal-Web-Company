namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Fila de stock de un artículo en un almacén puntual (OITW + OWHS) -- non-positional,
/// ver el doc-comment de ItemDto. Available se calcula acá, no en SAP.
/// </summary>
public sealed record WarehouseStockDto
{
    public string WhsCode { get; init; } = null!;
    public string WhsName { get; init; } = null!;
    public decimal OnHand { get; init; }
    public decimal IsCommited { get; init; }
    public decimal Available => OnHand - IsCommited;
}
