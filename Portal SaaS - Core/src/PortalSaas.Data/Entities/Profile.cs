namespace PortalSaas.Data.Entities;

/// <summary>
/// Agrupa un subconjunto de <see cref="PermissionAction"/> -- portado de PortalSAP_v2
/// (`PERFIL`). Ya NO es global a la plataforma (mismo criterio que MenuGroup.cs,
/// decisión revisada 26 jul 2026) -- <see cref="OrganizationId"/> null = plantilla de
/// plataforma (solo lectura para organizaciones); con valor = propio de esa
/// organización.
/// </summary>
public sealed class Profile
{
    public long Id { get; set; }

    /// <summary>Null = plantilla global de plataforma (solo lectura para organizaciones). Con valor = propio de esa organización.</summary>
    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public ICollection<ProfileAction> ProfileActions { get; set; } = new List<ProfileAction>();
    public ICollection<UserMenuProfile> UserMenuProfiles { get; set; } = new List<UserMenuProfile>();
}
