namespace PortalSaas.Data.Entities;

/// <summary>Una compañía/schema SAP -- equivalente a EMPRESA en PortalSAP_v2, ahora colgando de Organization. El código SAP (ej. "DEPOR") es una columna Code única, no la PK (ver docs/01-CONVENCION-NOMBRES-BD.md §3).</summary>
public sealed class Company
{
    // Generado en C#, no en la base -- ver Organization.Id.
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public long InstanceId { get; set; }
    public Instance Instance { get; set; } = null!;

    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DatabaseName { get; set; } = null!;
    public string ServiceLayerUrl { get; set; } = null!;
    public string IntegrationUsername { get; set; } = null!;

    /// <summary>Cifrado AES-256-GCM antes de guardar.</summary>
    public string IntegrationSecretKey { get; set; } = null!;

    public string Country { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}
