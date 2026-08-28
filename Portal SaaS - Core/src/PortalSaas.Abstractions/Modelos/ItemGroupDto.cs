namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de OITB (grupo de artículo) de la compañía SAP activa. Non-positional -- ver CustomerDto.</summary>
public sealed record ItemGroupDto
{
    public int ItemGroupCode { get; init; }
    public string ItemGroupName { get; init; } = null!;
}
