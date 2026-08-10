using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Comercial;

/// <summary>
/// Implementación real de IContractLimitService -- ver docs/03-MODELO-CORE-COMERCIAL.md
/// §5 para las reglas de negocio. Según Organization.Mode, consulta la suscripción
/// activa más reciente (saas, límites desde la tabla Plans local -- ahí sí es de
/// confianza porque la BD central es la fuente de verdad) o el token de licencia
/// firmado por el central (on_premise). En on-premise los límites NO se leen de la
/// tabla Plans local: esa tabla es editable a mano desde Admin/Plans en la propia
/// instalación, así que si se leyera de ahí bastaría con subir un UserLimit en la BD
/// local para burlar el contrato -- exactamente el mismo hueco que ya se cerró para
/// Status/ExpiresAt en OrganizationAccessGateService.CheckLicenseAsync. Los límites
/// viajan dentro del payload firmado (ver LicenseStatusPayload/LicenseActivationService)
/// y por eso una edición local sin la firma del central no altera nada.
/// </summary>
public sealed class ContractLimitService : IContractLimitService
{
    private readonly PortalSaasDbContext _db;
    private readonly ILicenseTokenService _tokenService;
    private readonly int _offlineGraceDays;

    public ContractLimitService(PortalSaasDbContext db, ILicenseTokenService tokenService, IConfiguration configuration)
    {
        _db = db;
        _tokenService = tokenService;
        _offlineGraceDays = configuration.GetValue<int?>("Licensing:OfflineGraceDays") ?? 15;
    }

    public async Task<LimitCheckResult> CheckUserLimitAsync(Guid organizationId, CancellationToken ct = default)
    {
        var limits = await GetActiveLimitsAsync(organizationId, ct);
        if (limits is not { } l)
        {
            return LimitCheckResult.Denied("La organización no tiene un plan vigente (suscripción o licencia) -- no se puede verificar el límite de usuarios.");
        }

        if (l.UserLimit is null)
        {
            return LimitCheckResult.Allowed();
        }

        var currentUsers = await _db.Users
            .Where(u => u.OrganizationId == organizationId && u.IsActive)
            .CountAsync(ct);

        return currentUsers >= l.UserLimit
            ? LimitCheckResult.Denied($"Límite de usuarios del plan '{l.PlanCode}' alcanzado ({currentUsers}/{l.UserLimit}).")
            : LimitCheckResult.Allowed();
    }

    public async Task<LimitCheckResult> CheckCompanyLimitAsync(Guid organizationId, CancellationToken ct = default)
    {
        var limits = await GetActiveLimitsAsync(organizationId, ct);
        if (limits is not { } l)
        {
            return LimitCheckResult.Denied("La organización no tiene un plan vigente (suscripción o licencia) -- no se puede verificar el límite de compañías.");
        }

        if (l.CompanyLimit is null)
        {
            return LimitCheckResult.Allowed();
        }

        var currentCompanies = await _db.Companies
            .Where(c => c.OrganizationId == organizationId && c.IsActive)
            .CountAsync(ct);

        return currentCompanies >= l.CompanyLimit
            ? LimitCheckResult.Denied($"Límite de compañías del plan '{l.PlanCode}' alcanzado ({currentCompanies}/{l.CompanyLimit}).")
            : LimitCheckResult.Allowed();
    }

    public async Task<LimitCheckResult> CheckMonthlyTransactionLimitAsync(Guid organizationId, string period, string metricName, CancellationToken ct = default)
    {
        var limits = await GetActiveLimitsAsync(organizationId, ct);
        if (limits is not { } l)
        {
            return LimitCheckResult.Denied("La organización no tiene un plan vigente (suscripción o licencia) -- no se puede verificar el límite de transacciones.");
        }

        if (l.MonthlyTransactionLimit is null)
        {
            return LimitCheckResult.Allowed();
        }

        var currentUsage = await _db.UsageMetrics
            .Where(m => m.OrganizationId == organizationId && m.Period == period && m.MetricName == metricName)
            .SumAsync(m => (decimal?)m.Value, ct) ?? 0m;

        return currentUsage >= l.MonthlyTransactionLimit
            ? LimitCheckResult.Denied($"Límite mensual de transacciones del plan '{l.PlanCode}' alcanzado ({currentUsage}/{l.MonthlyTransactionLimit}) para el período {period}.")
            : LimitCheckResult.Allowed();
    }

    private sealed record ActiveLimits(string PlanCode, int? UserLimit, int? CompanyLimit, int? MonthlyTransactionLimit);

    private async Task<ActiveLimits?> GetActiveLimitsAsync(Guid organizationId, CancellationToken ct)
    {
        var organization = await _db.Organizations.FindAsync([organizationId], ct);
        if (organization is null)
        {
            return null;
        }

        return organization.Mode == OrganizationMode.OnPremise
            ? await GetLimitsFromSignedLicenseAsync(organizationId, ct)
            : await GetLimitsFromSubscriptionAsync(organizationId, ct);
    }

    private async Task<ActiveLimits?> GetLimitsFromSubscriptionAsync(Guid organizationId, CancellationToken ct)
    {
        var subscription = await _db.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.OrganizationId == organizationId
                && (s.Status == SubscriptionStatus.Trial || s.Status == SubscriptionStatus.Active))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (subscription?.Plan is not { } plan)
        {
            return null;
        }

        return new ActiveLimits(plan.Code, plan.UserLimit, plan.CompanyLimit, plan.MonthlyTransactionLimit);
    }

    /// <summary>
    /// Misma validación de firma/vigencia/gracia offline que
    /// OrganizationAccessGateService.CheckLicenseAsync -- los límites deben salir del
    /// payload firmado, nunca de la fila local sin verificar (esa se puede editar a
    /// mano o restaurar desde un backup).
    /// </summary>
    private async Task<ActiveLimits?> GetLimitsFromSignedLicenseAsync(Guid organizationId, CancellationToken ct)
    {
        var license = await _db.OnPremiseLicenses
            .Include(l => l.Plan)
            .Where(l => l.OrganizationId == organizationId)
            .OrderByDescending(l => l.IssuedAt)
            .FirstOrDefaultAsync(ct);

        if (license is null || license.SignedStatusToken is not { Length: > 0 } || license.SignedStatusUpdatedAt is not { } lastUpdate)
        {
            return null;
        }

        if (!_tokenService.TryVerify(license.SignedStatusToken, out var payload) || payload is null)
        {
            return null;
        }

        if (DateTimeOffset.UtcNow - lastUpdate > TimeSpan.FromDays(_offlineGraceDays))
        {
            return null;
        }

        if (payload.Status != OnPremiseLicenseStatus.Active || payload.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        // El Code es solo para el mensaje al usuario -- no es un dato de seguridad,
        // así que usar el nombre local del plan acá es inofensivo.
        return new ActiveLimits(license.Plan.Code, payload.UserLimit, payload.CompanyLimit, payload.MonthlyTransactionLimit);
    }
}
