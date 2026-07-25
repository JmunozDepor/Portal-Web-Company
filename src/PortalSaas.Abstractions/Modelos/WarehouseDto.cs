namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de OWHS de la compañía SAP activa. Non-positional -- ver CustomerDto.</summary>
public sealed record WarehouseDto
{
    public string WarehouseCode { get; init; } = null!;
    public string WarehouseName { get; init; } = null!;
}
