namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de OCTG (condición de pago) de la compañía SAP activa. Non-positional -- ver CustomerDto.</summary>
public sealed record PaymentTermsDto
{
    public int PaymentTermsCode { get; init; }
    public string PaymentTermsName { get; init; } = null!;
}
