namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de OCRG (grupo de socio de negocio) de la compañía SAP activa. Non-positional -- ver CustomerDto.</summary>
public sealed record BusinessPartnerGroupDto
{
    public int GroupCode { get; init; }
    public string GroupName { get; init; } = null!;
}
