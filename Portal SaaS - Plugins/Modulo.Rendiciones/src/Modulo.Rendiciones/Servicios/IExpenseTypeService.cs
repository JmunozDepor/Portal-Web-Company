using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public interface IExpenseTypeService
{
    Task<IReadOnlyList<ExpenseType>> ListActiveAsync(Guid companyId, CancellationToken ct = default);

    Task<IReadOnlyList<ExpenseType>> ListAllAsync(Guid companyId, CancellationToken ct = default);

    Task<long> CreateAsync(Guid companyId, string name, string? sapGlAccount, bool isMileage, decimal? ratePerKm, CancellationToken ct = default);

    Task UpdateAsync(long id, Guid companyId, string name, string? sapGlAccount, bool isActive, bool isMileage, decimal? ratePerKm, CancellationToken ct = default);
}
