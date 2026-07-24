using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Comercial;

/// <summary>
/// Implementación real de IContractLimitService -- ver docs/03-MODELO-CORE-COMERCIAL.md
/// §5 para las reglas de negocio. Consulta la suscripción activa más reciente de la
/// organización (trial o active) y su plan; si no hay ninguna, deniega siempre (falla
/// hacia lo más estricto, nunca asume "sin límite").
/// </summary>
public sealed class ContractLimitService : IContractLimitService
{
    private readonly PortalSaasDbContext _db;

    public ContractLimitService(PortalSaasDbContext db)
    {
        _db = db;
    }

    public async Task<LimitCheckResult> CheckUserLimitAsync(Guid organizationId, CancellationToken ct = default)
    {
        var plan = await GetActivePlanAsync(organizationId, ct);
        if (plan is null)
        {
            return LimitCheckResult.Denied("La organización no tiene una suscripción activa -- no se puede verificar el límite de usuarios.");
        }

        if (plan.UserLimit is null)
        {
            return LimitCheckResult.Allowed();
        }

        var currentUsers = await _db.Users
            .Where(u => u.OrganizationId == organizationId && u.IsActive)
            .CountAsync(ct);

        return currentUsers >= plan.UserLimit
            ? LimitCheckResult.Denied($"Límite de usuarios del plan '{plan.Code}' alcanzado ({currentUsers}/{plan.UserLimit}).")
            : LimitCheckResult.Allowed();
    }

    public async Task<LimitCheckResult> CheckCompanyLimitAsync(Guid organizationId, CancellationToken ct = default)
    {
        var plan = await GetActivePlanAsync(organizationId, ct);
        if (plan is null)
        {
            return LimitCheckResult.Denied("La organización no tiene una suscripción activa -- no se puede verificar el límite de compañías.");
        }

        if (plan.CompanyLimit is null)
        {
            return LimitCheckResult.Allowed();
        }

        var currentCompanies = await _db.Companies
            .Where(c => c.OrganizationId == organizationId && c.IsActive)
            .CountAsync(ct);

        return currentCompanies >= plan.CompanyLimit
            ? LimitCheckResult.Denied($"Límite de compañías del plan '{plan.Code}' alcanzado ({currentCompanies}/{plan.CompanyLimit}).")
            : LimitCheckResult.Allowed();
    }

    public async Task<LimitCheckResult> CheckMonthlyTransactionLimitAsync(Guid organizationId, string period, string metricName, CancellationToken ct = default)
    {
        var plan = await GetActivePlanAsync(organizationId, ct);
        if (plan is null)
        {
            return LimitCheckResult.Denied("La organización no tiene una suscripción activa -- no se puede verificar el límite de transacciones.");
        }

        if (plan.MonthlyTransactionLimit is null)
        {
            return LimitCheckResult.Allowed();
        }

        var currentUsage = await _db.UsageMetrics
            .Where(m => m.OrganizationId == organizationId && m.Period == period && m.MetricName == metricName)
            .SumAsync(m => (decimal?)m.Value, ct) ?? 0m;

        return currentUsage >= plan.MonthlyTransactionLimit
            ? LimitCheckResult.Denied($"Límite mensual de transacciones del plan '{plan.Code}' alcanzado ({currentUsage}/{plan.MonthlyTransactionLimit}) para el período {period}.")
            : LimitCheckResult.Allowed();
    }

    private async Task<Plan?> GetActivePlanAsync(Guid organizationId, CancellationToken ct)
    {
        var subscription = await _db.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.OrganizationId == organizationId
                && (s.Status == SubscriptionStatus.Trial || s.Status == SubscriptionStatus.Active))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

        return subscription?.Plan;
    }
}
