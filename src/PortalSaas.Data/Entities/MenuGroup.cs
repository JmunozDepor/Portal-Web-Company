namespace PortalSaas.Data.Entities;

/// <summary>
/// Colección nombrada de nodos de <see cref="Menu"/> (visibilidad/navegación) --
/// portado de PortalSAP_v2 (`GRUPO_MENU`). GLOBAL a la plataforma, no lleva
/// `organization_id` propio -- el scope real por organización lo da
/// <see cref="UserMenuGroup"/> (usuario + compañía), ver
/// docs/03-MODELO-CORE-COMERCIAL.md §3.
/// </summary>
public sealed class MenuGroup
{
    public long Id { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<MenuGroupItem> MenuGroupItems { get; set; } = new List<MenuGroupItem>();
    public ICollection<UserMenuGroup> UserMenuGroups { get; set; } = new List<UserMenuGroup>();
}
