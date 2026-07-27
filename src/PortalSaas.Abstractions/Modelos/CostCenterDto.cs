namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Fila de OPRC (modelo de "5 dimensiones" de SAP B1, filtrado por DimCode) de la
/// compañía SAP activa -- solo la dimensión "Centro de Costos", ver
/// ICostCenterCatalogService para el detalle del DimCode. Non-positional -- ver
/// CustomerDto.
/// </summary>
public sealed record CostCenterDto
{
    public string Code { get; init; } = null!;
    public string Name { get; init; } = null!;
}
