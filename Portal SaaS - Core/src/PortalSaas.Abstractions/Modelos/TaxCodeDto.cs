namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de OVTG (código de impuesto) de la compañía SAP activa. Non-positional -- ver CustomerDto.</summary>
public sealed record TaxCodeDto
{
    public string Code { get; init; } = null!;
    public string Name { get; init; } = null!;
    public decimal Rate { get; init; }
}
