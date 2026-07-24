namespace PortalSaas.Data.Entities;

/// <summary>
/// Agrupa un subconjunto de <see cref="PermissionAction"/> -- portado de PortalSAP_v2
/// (`PERFIL`). GLOBAL a la plataforma, no lleva `organization_id` (ver MenuGroup.cs).
/// </summary>
public sealed class Profile
{
    public long Id { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public ICollection<ProfileAction> ProfileActions { get; set; } = new List<ProfileAction>();
    public ICollection<UserMenuProfile> UserMenuProfiles { get; set; } = new List<UserMenuProfile>();
}
