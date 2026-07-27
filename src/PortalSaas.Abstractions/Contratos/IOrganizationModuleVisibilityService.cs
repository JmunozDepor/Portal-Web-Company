using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Self-service para que el admin de la organización actual (ver
/// ICurrentUserContext.OrganizationId) oculte del árbol de menú un módulo que sí tiene
/// contratado pero no usa -- capa aparte de IContractLimitService/IModuleAccessService
/// (comercial, qué pagó). Nunca cambia lo que la organización contrató, solo lo que ve.
/// </summary>
public interface IOrganizationModuleVisibilityService
{
    /// <summary>Todos los módulos CONTRATADOS por la organización actual, con su estado de oculto.</summary>
    Task<IReadOnlyList<ModuleVisibilityDto>> ListAsync(CancellationToken ct = default);

    Task SetHiddenAsync(long moduleId, bool isHidden, CancellationToken ct = default);
}
