using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public interface IDocumentTypeService
{
    Task<IReadOnlyList<DocumentType>> ListActiveAsync(Guid companyId, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentType>> ListAllAsync(Guid companyId, CancellationToken ct = default);

    Task<long> CreateAsync(Guid companyId, string name, bool appliesTax, decimal taxPercentage, CancellationToken ct = default);

    Task UpdateAsync(long id, Guid companyId, string name, bool appliesTax, decimal taxPercentage, bool isActive, CancellationToken ct = default);
}
