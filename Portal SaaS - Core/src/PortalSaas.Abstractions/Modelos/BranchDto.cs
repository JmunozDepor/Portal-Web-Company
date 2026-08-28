namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de OBPL (sucursal) de la compañía SAP activa. Non-positional -- ver CustomerDto.</summary>
public sealed record BranchDto
{
    public int BranchCode { get; init; }
    public string BranchName { get; init; } = null!;
}
