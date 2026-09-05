namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de OPLN (catálogo de listas de precios) -- non-positional, ver el doc-comment de ItemDto.</summary>
public sealed record PriceListOptionDto
{
    public int ListNum { get; init; }
    public string ListName { get; init; } = null!;
}
