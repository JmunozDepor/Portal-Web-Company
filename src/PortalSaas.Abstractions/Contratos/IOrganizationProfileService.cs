using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// CRUD de perfiles de la organización actual -- mismo criterio que
/// IOrganizationMenuGroupService (incluye plantillas globales de solo lectura).
/// </summary>
public interface IOrganizationProfileService
{
    Task<IReadOnlyList<OrganizationProfileDto>> ListAsync(CancellationToken ct = default);

    Task<OrganizationProfileDto?> GetAsync(long id, CancellationToken ct = default);

    Task<long> CreateAsync(string name, string? description, IReadOnlyList<long> actionIds, CancellationToken ct = default);

    /// <summary>Rechaza con InvalidOperationException si el perfil no pertenece a la organización actual (incluida una plantilla global).</summary>
    Task UpdateAsync(long id, string name, string? description, IReadOnlyList<long> actionIds, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>Catálogo fijo de PermissionAction (Ver/Crear/Editar/Eliminar/Aprobar/Exportar) -- para armar los checkboxes de un perfil.</summary>
    Task<IReadOnlyList<PermissionActionDto>> ListAllActionsAsync(CancellationToken ct = default);
}
