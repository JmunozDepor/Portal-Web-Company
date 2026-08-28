namespace PortalSaas.Data.Entities;

/// <summary>
/// Árbol jerárquico N-niveles, autorreferencial -- portado de PortalSAP_v2 (`MENU`).
/// `PagePath = null` -> nodo de agrupación (no navegable, no puede recibir Profile).
/// `PagePath != null` -> hoja navegable, el módulo final embebido en el shell.
/// `OriginModule` identifica qué plugin es dueño de esta entrada -- sincronizado por
/// upsert al arrancar el host (ver IModuloPortal.GetMenu(), MenuSyncService). GLOBAL a
/// la plataforma (mismo criterio que MenuGroup), no lleva `organization_id`.
/// </summary>
public sealed class Menu
{
    public long Id { get; set; }

    public long? ParentMenuId { get; set; }
    public Menu? ParentMenu { get; set; }

    /// <summary>IModuloPortal.ModuleCode del plugin dueño de este nodo.</summary>
    public string OriginModule { get; set; } = null!;

    /// <summary>Único DENTRO de OriginModule -- ver índice único (origin_module, code).</summary>
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string? Icon { get; set; }
    public string? PagePath { get; set; }
    public int Order { get; set; }

    /// <summary>Profundidad denormalizada (0 = raíz).</summary>
    public int Level { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Menu> Children { get; set; } = new List<Menu>();
    public ICollection<MenuGroupItem> MenuGroupItems { get; set; } = new List<MenuGroupItem>();
    public ICollection<UserMenuProfile> UserMenuProfiles { get; set; } = new List<UserMenuProfile>();
}
