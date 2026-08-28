using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public interface IServiceConfigService
{
    Task<IReadOnlyList<WmsServiceConfig>> ListAllAsync(Guid companyId, CancellationToken ct = default);

    Task<long> CreateAsync(Guid companyId, string configKey, string? configValue, bool isActive, string updatedBy, CancellationToken ct = default);

    Task UpdateAsync(long id, Guid companyId, string? configValue, bool isActive, string updatedBy, CancellationToken ct = default);
}
