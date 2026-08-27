using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Catálogo de conexiones externas por compañía + binding módulo→conexión.
/// Todos los métodos reciben organizationId y companyId explícitos: la página
/// /Admin los toma de la ruta; el plugin los toma de ICurrentUserContext + selector.
/// El servicio valida que companyId pertenezca a organizationId.
/// </summary>
public interface ICompanyExternalConnectionService
{
    Task<IReadOnlyList<ExternalConnectionDto>> ListAsync(Guid organizationId, Guid companyId, CancellationToken ct = default);
    Task<ExternalConnectionDto?> GetAsync(Guid organizationId, Guid companyId, long id, CancellationToken ct = default);
    Task<long> CreateAsync(Guid organizationId, Guid companyId, ExternalConnectionEditModel model, CancellationToken ct = default);
    Task UpdateAsync(Guid organizationId, Guid companyId, long id, ExternalConnectionEditModel model, CancellationToken ct = default);
    Task DeleteAsync(Guid organizationId, Guid companyId, long id, CancellationToken ct = default);
    Task<ConnectionTestResultDto> TestAsync(Guid organizationId, Guid companyId, long id, CancellationToken ct = default);

    Task<IReadOnlyList<ModuleConnectionBindingDto>> ListBindingsAsync(Guid organizationId, Guid companyId, CancellationToken ct = default);
    Task SetBindingAsync(Guid organizationId, Guid companyId, string moduleCode, string purpose, long connectionId, CancellationToken ct = default);
    Task ClearBindingAsync(Guid organizationId, Guid companyId, string moduleCode, string purpose, CancellationToken ct = default);
}
