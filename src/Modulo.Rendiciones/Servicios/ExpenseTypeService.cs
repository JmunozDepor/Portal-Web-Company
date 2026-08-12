using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class ExpenseTypeService : IExpenseTypeService
{
    private readonly RendicionesDbContext _db;

    public ExpenseTypeService(RendicionesDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ExpenseType>> ListActiveAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.ExpenseTypes
            .Where(t => t.CompanyId == companyId && t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExpenseType>> ListAllAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.ExpenseTypes
            .Where(t => t.CompanyId == companyId)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public async Task<long> CreateAsync(Guid companyId, string name, string? sapGlAccount, bool isMileage, decimal? ratePerKm, CancellationToken ct = default)
    {
        var type = new ExpenseType
        {
            CompanyId = companyId,
            Name = name,
            SapGlAccount = sapGlAccount,
            IsActive = true,
            IsMileage = isMileage,
            RatePerKm = isMileage ? ratePerKm : null,
        };
        _db.ExpenseTypes.Add(type);
        await _db.SaveChangesAsync(ct);
        return type.Id;
    }

    public async Task UpdateAsync(long id, Guid companyId, string name, string? sapGlAccount, bool isActive, bool isMileage, decimal? ratePerKm, CancellationToken ct = default)
    {
        var type = await _db.ExpenseTypes.FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("El tipo de gasto no existe o no pertenece a esta compañía.");

        type.Name = name;
        type.SapGlAccount = sapGlAccount;
        type.IsActive = isActive;
        type.IsMileage = isMileage;
        type.RatePerKm = isMileage ? ratePerKm : null;
        await _db.SaveChangesAsync(ct);
    }
}
