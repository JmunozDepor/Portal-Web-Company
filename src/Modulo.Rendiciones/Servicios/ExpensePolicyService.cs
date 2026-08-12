using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class ExpensePolicyService : IExpensePolicyService
{
    private readonly RendicionesDbContext _db;

    public ExpensePolicyService(RendicionesDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ExpensePolicy>> ListAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.ExpensePolicies
            .Include(p => p.ExpenseType)
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.ExpenseType!.Name)
            .ToListAsync(ct);

    public async Task<long> CreateAsync(Guid companyId, long expenseTypeId, decimal? maxAmount, bool isBlocking, CancellationToken ct = default)
    {
        var alreadyExists = await _db.ExpensePolicies.AnyAsync(p => p.CompanyId == companyId && p.ExpenseTypeId == expenseTypeId, ct);
        if (alreadyExists)
            throw new InvalidOperationException("Ese tipo de gasto ya tiene una política -- editá la existente en vez de crear otra.");

        var policy = new ExpensePolicy
        {
            CompanyId = companyId,
            ExpenseTypeId = expenseTypeId,
            MaxAmount = maxAmount,
            IsBlocking = isBlocking,
            IsActive = true,
        };
        _db.ExpensePolicies.Add(policy);
        await _db.SaveChangesAsync(ct);
        return policy.Id;
    }

    public async Task UpdateAsync(long id, Guid companyId, decimal? maxAmount, bool isBlocking, bool isActive, CancellationToken ct = default)
    {
        var policy = await _db.ExpensePolicies.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("La política no existe o no pertenece a esta compañía.");

        policy.MaxAmount = maxAmount;
        policy.IsBlocking = isBlocking;
        policy.IsActive = isActive;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default)
    {
        var policy = await _db.ExpensePolicies.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId, ct);
        if (policy is null)
            return;

        _db.ExpensePolicies.Remove(policy);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ExpenseValidationResult> ValidateAsync(Guid companyId, Guid userId, long? expenseTypeId, decimal amount,
        string? documentNumber, string? supplierTaxId, long? expenseIdToExclude, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var blocked = false;

        if (expenseTypeId is { } typeId)
        {
            var policy = await _db.ExpensePolicies
                .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.ExpenseTypeId == typeId && p.IsActive, ct);

            if (policy?.MaxAmount is { } cap && amount > cap)
            {
                if (policy.IsBlocking)
                {
                    blocked = true;
                    warnings.Add($"El monto ({amount:N0}) supera el tope permitido para esta categoría ({cap:N0}).");
                }
                else
                {
                    warnings.Add($"El monto ({amount:N0}) supera el tope sugerido para esta categoría ({cap:N0}) -- se guardó igual, es solo una advertencia.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(documentNumber))
        {
            var isDuplicate = await _db.ExpenseReportLines.AnyAsync(d =>
                d.CompanyId == companyId && d.UserId == userId && d.DocumentNumber == documentNumber &&
                (supplierTaxId == null || d.SupplierTaxId == supplierTaxId) &&
                (expenseIdToExclude == null || d.Id != expenseIdToExclude.Value), ct);

            if (isDuplicate)
                warnings.Add($"Ya tenés otro gasto con el número de documento \"{documentNumber}\" -- revisá que no sea un duplicado (por ejemplo, de una importación por OCR repetida).");
        }

        return new ExpenseValidationResult(blocked, warnings);
    }
}
