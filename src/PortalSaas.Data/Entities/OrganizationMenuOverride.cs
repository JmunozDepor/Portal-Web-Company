namespace PortalSaas.Data.Entities;

/// <summary>
/// Override por organización de nombre/orden/visibilidad de un nodo de `Menu` --
/// capa separada del catálogo global (mismo criterio que OrganizationModuleVisibility
/// para módulos completos, acá a nivel de nodo individual). Sin fila = usa el valor
/// original de Menu (nombre/orden sin cambios, visible).
/// </summary>
public sealed class OrganizationMenuOverride
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public long MenuId { get; set; }
    public Menu Menu { get; set; } = null!;

    /// <summary>Null = usa Menu.Name sin cambios.</summary>
    public string? CustomLabel { get; set; }

    /// <summary>Null = usa Menu.Order sin cambios.</summary>
    public int? CustomOrder { get; set; }

    public bool IsHidden { get; set; }
}
