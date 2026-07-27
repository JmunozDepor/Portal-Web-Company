namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de OUOM (unidad de medida) de la compañía SAP activa. Non-positional -- ver CustomerDto.</summary>
public sealed record UnitOfMeasureDto
{
    public int UomCode { get; init; }
    public string UomName { get; init; } = null!;
}
