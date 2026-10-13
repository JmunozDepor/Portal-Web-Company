using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Base de datos externa de cada módulo de una compañía: Instancia (servidor de la
/// organización) + nombre de la base + credencial propia opcional. Relación 1 a 1
/// (compañía, módulo). Todos los métodos reciben organizationId y companyId explícitos:
/// /Admin los toma de la ruta; el plugin Modulo.Administracion, de ICurrentUserContext +
/// selector. El servicio valida que companyId pertenezca a organizationId.
/// </summary>
public interface ICompanyExternalConnectionService
{
    /// <summary>Instancias ACTIVAS de la organización (cualquier motor).</summary>
    Task<IReadOnlyList<DatabaseInstanceOptionDto>> ListInstancesAsync(Guid organizationId, Guid companyId, CancellationToken ct = default);

    /// <summary>Una fila por módulo cargado con RequiresExternalDatabase, asignado o no.</summary>
    Task<IReadOnlyList<ModuleConnectionBindingDto>> ListBindingsAsync(Guid organizationId, Guid companyId, CancellationToken ct = default);

    /// <summary>null si el módulo no está cargado o no usa base externa.</summary>
    Task<ModuleConnectionBindingDto?> GetBindingAsync(Guid organizationId, Guid companyId, string moduleCode, CancellationToken ct = default);

    Task SetBindingAsync(Guid organizationId, Guid companyId, string moduleCode, ModuleDatabaseBindingInput input, CancellationToken ct = default);
    Task ClearBindingAsync(Guid organizationId, Guid companyId, string moduleCode, CancellationToken ct = default);
    Task<ConnectionTestResultDto> TestBindingAsync(Guid organizationId, Guid companyId, string moduleCode, CancellationToken ct = default);
}
