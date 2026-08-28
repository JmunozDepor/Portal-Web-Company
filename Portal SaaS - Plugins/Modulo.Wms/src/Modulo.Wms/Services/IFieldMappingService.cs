using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public interface IFieldMappingService
{
    Task<IReadOnlyList<WmsFieldMapping>> ListAllAsync(Guid companyId, CancellationToken ct = default);

    Task<long> CreateAsync(Guid companyId, string mapperKey, string fieldName, string valueTemplate, bool isActive, string updatedBy, CancellationToken ct = default);

    Task UpdateAsync(long id, Guid companyId, string valueTemplate, bool isActive, string updatedBy, CancellationToken ct = default);
}
