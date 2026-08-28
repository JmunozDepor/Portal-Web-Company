namespace PortalSaas.Data.Entities;

/// <summary>
/// Override por organización de si un módulo CONTRATADO (core, del plan, o add-on --
/// ver IModuleAccessService.GetContractedModuleCodesAsync) queda OCULTO del árbol de
/// menú para esa organización -- capa separada de OrganizationModule (que es
/// comercial: qué pagó) y de PlatformModule.IsCore. Un admin de organización puede
/// ocultar un módulo que sí tiene contratado pero no usa, sin que eso cambie lo que
/// paga. Sin fila = visible (no oculto) -- mismo criterio "falla hacia lo más
/// permisivo por default, explícito solo para ocultar" que ya usa
/// OrganizationDocumentPermission para el caso inverso.
/// </summary>
public sealed class OrganizationModuleVisibility
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public long ModuleId { get; set; }
    public PlatformModule Module { get; set; } = null!;

    public bool IsHidden { get; set; }
}
