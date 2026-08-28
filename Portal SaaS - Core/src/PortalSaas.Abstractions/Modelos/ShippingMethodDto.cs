namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de OSHP (método de envío) de la compañía SAP activa. Non-positional -- ver CustomerDto.</summary>
public sealed record ShippingMethodDto
{
    public int ShippingMethodCode { get; init; }
    public string ShippingMethodName { get; init; } = null!;
}
