using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public interface ICityService
{
    Task<IReadOnlyList<City>> ListActiveAsync(Guid companyId, CancellationToken ct = default);

    Task<IReadOnlyList<City>> ListAllAsync(Guid companyId, CancellationToken ct = default);

    Task<long> CreateAsync(Guid companyId, string name, CancellationToken ct = default);

    Task UpdateAsync(long id, Guid companyId, string name, bool isActive, CancellationToken ct = default);
}
