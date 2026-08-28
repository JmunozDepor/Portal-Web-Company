using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class ExpenseFundService : IExpenseFundService
{
    private readonly RendicionesDbContext _db;

    public ExpenseFundService(RendicionesDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ExpenseFund>> ListByUserAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
        await _db.ExpenseFunds
            .Where(f => f.CompanyId == companyId && f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

    public async Task<long> CreateAsync(ExpenseFund fund, CancellationToken ct = default)
    {
        _db.ExpenseFunds.Add(fund);
        await _db.SaveChangesAsync(ct);
        return fund.Id;
    }

    public async Task<decimal> CalculatePendingBalanceAsync(long fundId, Guid companyId, CancellationToken ct = default)
    {
        var fund = await _db.ExpenseFunds.FirstOrDefaultAsync(f => f.Id == fundId && f.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("El fondo por rendir no existe o no pertenece a esta compañía.");

        var totalSettled = await _db.ExpenseReportLines
            .Where(d => d.ExpenseReport!.ExpenseFundId == fundId && d.ExpenseReport.Status == "Approved")
            .SumAsync(d => (decimal?)d.Amount, ct) ?? 0m;

        return fund.Amount - totalSettled;
    }

    public async Task RecalculateStatusAsync(long fundId, Guid companyId, CancellationToken ct = default)
    {
        var fund = await _db.ExpenseFunds.FirstOrDefaultAsync(f => f.Id == fundId && f.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("El fondo por rendir no existe o no pertenece a esta compañía.");

        if (fund.Status == "Overdue")
            return;

        var balance = await CalculatePendingBalanceAsync(fundId, companyId, ct);
        fund.Status = balance <= 0 ? "Settled" : "Open";
        await _db.SaveChangesAsync(ct);
    }
}
