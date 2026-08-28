using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// CRUD de grupos de menú de la organización actual (ver ICurrentUserContext.
/// OrganizationId) -- self-service, consumido por Modulo.Administracion. Incluye,
/// además, las plantillas globales de plataforma (OrganizationId null) como filas de
/// solo lectura para que el admin pueda ver/clonar, pero nunca editarlas/eliminarlas
/// desde acá (eso sigue siendo exclusivo de /Admin/MenuGroups).
/// </summary>
public interface IOrganizationMenuGroupService
{
    Task<IReadOnlyList<OrganizationMenuGroupDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Null si el id no existe o pertenece a otra organización.</summary>
    Task<OrganizationMenuGroupDto?> GetAsync(long id, CancellationToken ct = default);

    Task<long> CreateAsync(string name, string? description, Guid? companyId, IReadOnlyList<long> menuIds, IReadOnlyDictionary<long, long?>? defaultProfileByMenu = null, CancellationToken ct = default);

    /// <summary>Rechaza con InvalidOperationException si el grupo no pertenece a la organización actual (incluida una plantilla global).</summary>
    Task UpdateAsync(long id, string name, string? description, bool isActive, Guid? companyId, IReadOnlyList<long> menuIds, IReadOnlyDictionary<long, long?>? defaultProfileByMenu = null, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);
}
