namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Fila de OITM de la compañía SAP activa. Non-positional -- ver el doc-comment de
/// CustomerDto (mismo motivo: mapeo por reflection de IHanaService.QueryAsync). La
/// tabla real puede tener cientos de miles de filas -- nunca se lista completa, solo
/// se busca (ver IItemCatalogService.SearchAsync).
/// </summary>
public sealed record ItemDto
{
    public string ItemCode { get; init; } = null!;
    public string ItemName { get; init; } = null!;
}
