using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Catálogo de Perfiles de Política + asignación por usuario. Las dos cosas viven en
/// un solo servicio a propósito (el catálogo y "quién tiene cuál" son la misma
/// responsabilidad pequeña) -- no confundir con IRendicionesUserRoleService, que
/// administra los 3 roles de acceso del módulo.
/// </summary>
public interface IPolicyProfileService
{
    Task<IReadOnlyList<PolicyProfile>> ListActiveAsync(Guid companyId, CancellationToken ct = default);

    Task<IReadOnlyList<PolicyProfile>> ListAllAsync(Guid companyId, CancellationToken ct = default);

    Task<long> CreateAsync(Guid companyId, string name, CancellationToken ct = default);

    Task UpdateAsync(long id, Guid companyId, string name, bool isActive, CancellationToken ct = default);

    /// <summary>Usado por Tasks 6/8 para resolver qué perfil aplica al calcular topes.</summary>
    Task<long?> GetAssignedProfileIdAsync(Guid companyId, Guid userId, CancellationToken ct = default);

    /// <summary>Upsert: `null` quita el perfil asignado (si lo hay); si no, crea o reemplaza
    /// la única fila activa de (CompanyId, UserId).</summary>
    Task AssignAsync(Guid companyId, Guid userId, long? policyProfileId, CancellationToken ct = default);

    /// <summary>Usado por UsuariosRoles/Index para mostrar el perfil de cada fila sin N+1.</summary>
    Task<IReadOnlyDictionary<Guid, PolicyProfile>> ListAssignmentsAsync(Guid companyId, CancellationToken ct = default);
}
