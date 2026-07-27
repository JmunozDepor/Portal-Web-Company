namespace PortalSaas.Data.Entities;

/// <summary>
/// Colección nombrada de nodos de <see cref="Menu"/> (visibilidad/navegación) --
/// portado de PortalSAP_v2 (`GRUPO_MENU`). Ya NO es global a la plataforma (decisión
/// revisada 26 jul 2026, ver CLAUDE.md "Grupos de Menú + Perfiles"): en el original
/// (mono-tenant) "global" y "de la organización" eran lo mismo, pero en este proyecto
/// multi-tenant un grupo global sería visible/editable por cualquier organización.
/// <see cref="OrganizationId"/> null = plantilla de plataforma (cargada por el
/// operador, de solo lectura para las organizaciones -- sirve para clonar); con valor
/// = propio de esa organización, creado/editado por su propio admin.
/// <see cref="CompanyId"/> null = aplica a cualquier compañía de la organización; con
/// valor = específico de esa compañía (una organización con 2+ SAP puede necesitar
/// agrupaciones de menú distintas por compañía). El scope real de la ASIGNACIÓN a un
/// usuario sigue siendo <see cref="UserMenuGroup"/> (usuario + compañía).
/// </summary>
public sealed class MenuGroup
{
    public long Id { get; set; }

    /// <summary>Null = plantilla global de plataforma (solo lectura para organizaciones). Con valor = propio de esa organización.</summary>
    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    /// <summary>Null = aplica a cualquier compañía de la organización. Con valor = específico de esa compañía.</summary>
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<MenuGroupItem> MenuGroupItems { get; set; } = new List<MenuGroupItem>();
    public ICollection<UserMenuGroup> UserMenuGroups { get; set; } = new List<UserMenuGroup>();
}
