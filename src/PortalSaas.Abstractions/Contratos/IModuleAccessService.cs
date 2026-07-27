namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Resuelve qué módulos comerciales (PortalSaas.Data.Entities.PlatformModule.Code)
/// tiene contratados una organización -- usado por IMenuNavigationService para ocultar
/// del árbol de menú los nodos (Menu.OriginModule) de un módulo no contratado. Un
/// Menu.OriginModule que no aparezca en el catálogo de PlatformModule.Code (ej.
/// "Administracion", "Manual") no está gateado por esto -- se trata como núcleo de
/// plataforma, no como add-on vendible.
/// </summary>
public interface IModuleAccessService
{
    /// <summary>Todos los PlatformModule.Code catalogados -- para distinguir "módulo sin contratar" de "módulo no comercial" (nunca gateado).</summary>
    Task<IReadOnlySet<string>> GetCatalogedModuleCodesAsync(CancellationToken ct = default);

    /// <summary>
    /// PlatformModule.Code contratados por la organización -- unión de los módulos
    /// core (IsCore=true, incluidos en todo plan), los del plan activo (saas:
    /// Subscription vigente / on_premise: OnPremiseLicense vigente, mismo criterio
    /// "por modo" que IContractLimitService) y los add-ons propios de la organización
    /// (OrganizationModule).
    /// </summary>
    Task<IReadOnlySet<string>> GetContractedModuleCodesAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// PlatformModule.Code que el admin de la organización ocultó explícitamente (ver
    /// OrganizationModuleVisibility) -- capa aparte de "contratado": un módulo puede
    /// estar contratado y a la vez oculto (la organización pagó por él pero no lo usa).
    /// Sin fila = no oculto (falla hacia lo más permisivo por default; ocultar es
    /// siempre una acción explícita del admin, nunca un estado inicial).
    /// </summary>
    Task<IReadOnlySet<string>> GetHiddenModuleCodesAsync(Guid organizationId, CancellationToken ct = default);
}
