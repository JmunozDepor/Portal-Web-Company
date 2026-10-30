using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class ExpensePolicyService : IExpensePolicyService
{
    private readonly RendicionesDbContext _db;
    private readonly IPolicyProfileService _profiles;

    public ExpensePolicyService(RendicionesDbContext db, IPolicyProfileService profiles)
    {
        _db = db;
        _profiles = profiles;
    }

    public async Task<IReadOnlyList<ExpensePolicy>> ListAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.ExpensePolicies
            .Include(p => p.ExpenseType)
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.ExpenseType!.Name)
            .ToListAsync(ct);

    public async Task<long> CreateAsync(Guid companyId, long expenseTypeId, long? cityId, long? policyProfileId, string period,
        decimal? maxAmount, CancellationToken ct = default)
    {
        if (period == ExpensePolicyPeriod.Weekly)
            cityId = null;

        // I6 (revisión final, 2026-10-29): Period entra al guard -- un PerDocument y un
        // Weekly para EXACTAMENTE la misma (tipo, ciudad, perfil) son dos políticas
        // legítimamente distintas (ej. Combustible para "Supervisor en terreno" con tope por
        // boleta Y tope semanal acumulado, spec Decisión 7). Antes de este fix, la segunda
        // política chocaba contra este guard aunque tuviera un Period distinto.
        var alreadyExists = await _db.ExpensePolicies.AnyAsync(p =>
            p.CompanyId == companyId && p.ExpenseTypeId == expenseTypeId &&
            p.CityId == cityId && p.PolicyProfileId == policyProfileId && p.Period == period, ct);
        if (alreadyExists)
            throw new InvalidOperationException("Ya existe una política para ese tipo de gasto con exactamente esa misma ciudad, perfil y periodo -- editá la existente en vez de crear otra.");

        var policy = new ExpensePolicy
        {
            CompanyId = companyId,
            ExpenseTypeId = expenseTypeId,
            CityId = cityId,
            PolicyProfileId = policyProfileId,
            Period = period,
            MaxAmount = maxAmount,
            IsActive = true,
        };
        _db.ExpensePolicies.Add(policy);
        await _db.SaveChangesAsync(ct);
        return policy.Id;
    }

    public async Task UpdateAsync(long id, Guid companyId, decimal? maxAmount, bool isActive, CancellationToken ct = default)
    {
        var policy = await _db.ExpensePolicies.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("La política no existe o no pertenece a esta compañía.");

        policy.MaxAmount = maxAmount;
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

    public async Task<ExpenseValidationResult> ValidateAsync(Guid companyId, Guid userId, long? expenseTypeId, long? cityId,
        decimal amount, decimal quantity, string? documentNumber, string? supplierTaxId, long? expenseIdToExclude,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var reimbursement = amount;

        if (expenseTypeId is { } typeId)
        {
            var userProfileId = await _profiles.GetAssignedProfileIdAsync(companyId, userId, ct);
            var expenseType = await _db.ExpenseTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == typeId, ct);

            // I1: la cantidad solo tiene sentido para un tipo que la requiere -- el campo
            // Cantidad del formulario solo se oculta con display:none cuando se cambia de
            // categoría, nunca se limpia, así que un valor obsoleto (ej. "3" dejado de un
            // tipo anterior) seguía llegando acá sin que ValidateAsync chequeara
            // RequiresQuantity, multiplicando el tope de un tipo que no debía verse afectado.
            // Nunca confiar en lo que mandó el llamador: se resuelve acá, el único lugar que
            // calcula el tope. Una cantidad <= 0 (0, negativa) nunca debe multiplicar el tope
            // -- se trata como 1 en ese caso también.
            var effectiveQuantity = expenseType?.RequiresQuantity == true && quantity > 0 ? quantity : 1m;

            var policy = await FindApplicablePolicyAsync(companyId, typeId, cityId, userProfileId, ExpensePolicyPeriod.PerDocument, ct);

            if (policy?.MaxAmount is { } maxAmount)
            {
                var tope = maxAmount * effectiveQuantity;
                if (amount > tope)
                {
                    reimbursement = tope;
                    var city = cityId is { } cid ? await _db.Cities.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct) : null;
                    var tipoNombre = expenseType?.Name ?? "este tipo de gasto";
                    warnings.Add($"El monto ({amount:N0}) supera el tope de {tipoNombre}{(city is not null ? $" en {city.Name}" : "")} ({tope:N0}) -- se reembolsará {reimbursement:N0}.");
                }
            }
        }

        // I1b: backstop final, independiente de cómo se haya llegado hasta acá -- el monto a
        // reembolsar nunca puede ser negativo (ej. si alguna combinación rara produjera un
        // tope negativo) ni superar el monto de la boleta.
        if (reimbursement < 0)
            reimbursement = 0;
        if (reimbursement > amount)
            reimbursement = amount;

        if (!string.IsNullOrWhiteSpace(documentNumber))
        {
            var isDuplicate = await _db.ExpenseReportLines.AnyAsync(d =>
                d.CompanyId == companyId && d.UserId == userId && d.DocumentNumber == documentNumber &&
                (supplierTaxId == null || d.SupplierTaxId == supplierTaxId) &&
                (expenseIdToExclude == null || d.Id != expenseIdToExclude.Value), ct);

            if (isDuplicate)
                warnings.Add($"Ya tenés otro gasto con el número de documento \"{documentNumber}\" -- revisá que no sea un duplicado (por ejemplo, de una importación por OCR repetida).");
        }

        return new ExpenseValidationResult(reimbursement, warnings);
    }

    public async Task<ExpensePolicy?> FindApplicablePolicyAsync(Guid companyId, long expenseTypeId, long? cityId,
        long? userProfileId, string period, CancellationToken ct = default)
    {
        // Trae de una sola query las políticas activas de (companyId, expenseTypeId, period)
        // que son candidatas (CityId/PolicyProfileId nulos "comodín", o iguales a lo pedido) y
        // elige en memoria la de mayor especificidad -- el volumen por tipo es bajo, no hace
        // falta resolver esto con 4 queries ni con SQL puro (spec, Decisión 4).
        var candidates = await _db.ExpensePolicies
            .Where(p => p.CompanyId == companyId && p.ExpenseTypeId == expenseTypeId && p.Period == period && p.IsActive)
            .Where(p => (p.CityId == null || p.CityId == cityId) &&
                        (p.PolicyProfileId == null || p.PolicyProfileId == userProfileId))
            .ToListAsync(ct);

        return candidates
            .OrderByDescending(Specificity)
            .FirstOrDefault();
    }

    /// <summary>
    /// Orden de especificidad del spec (Decisión 4): ciudad+perfil &gt; ciudad sola &gt;
    /// perfil solo &gt; ninguno. Solo se invoca sobre candidatos ya filtrados como elegibles
    /// por FindApplicablePolicyAsync.
    /// </summary>
    private static int Specificity(ExpensePolicy policy) => (policy.CityId is not null, policy.PolicyProfileId is not null) switch
    {
        (true, true) => 4,
        (true, false) => 3,
        (false, true) => 2,
        (false, false) => 1,
    };
}
