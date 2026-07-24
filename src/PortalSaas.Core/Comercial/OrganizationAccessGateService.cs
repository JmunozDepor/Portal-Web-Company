using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Comercial;

/// <summary>Implementación real de IOrganizationAccessGateService.</summary>
public sealed class OrganizationAccessGateService : IOrganizationAccessGateService
{
    private readonly PortalSaasDbContext _db;

    public OrganizationAccessGateService(PortalSaasDbContext db)
    {
        _db = db;
    }

    public async Task<LimitCheckResult> CheckAccessAsync(Guid organizationId, CancellationToken ct = default)
    {
        var organization = await _db.Organizations.FindAsync([organizationId], ct);
        if (organization is null)
        {
            return LimitCheckResult.Denied("La organización no existe.");
        }

        return organization.Mode == OrganizationMode.OnPremise
            ? await CheckLicenseAsync(organizationId, ct)
            : await CheckSubscriptionAsync(organizationId, ct);
    }

    private async Task<LimitCheckResult> CheckLicenseAsync(Guid organizationId, CancellationToken ct)
    {
        var license = await _db.OnPremiseLicenses
            .Where(l => l.OrganizationId == organizationId)
            .OrderByDescending(l => l.IssuedAt)
            .FirstOrDefaultAsync(ct);

        if (license is null)
        {
            return LimitCheckResult.Denied("Esta instalación no tiene una licencia asignada.");
        }

        if (license.Status != OnPremiseLicenseStatus.Active)
        {
            return LimitCheckResult.Denied($"La licencia de esta instalación está {license.Status} -- contacta a tu proveedor.");
        }

        if (license.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return LimitCheckResult.Denied("La licencia de esta instalación venció -- contacta a tu proveedor.");
        }

        return LimitCheckResult.Allowed();
    }

    private async Task<LimitCheckResult> CheckSubscriptionAsync(Guid organizationId, CancellationToken ct)
    {
        var subscription = await _db.Subscriptions
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (subscription is null)
        {
            return LimitCheckResult.Denied("Esta organización no tiene una suscripción -- contacta a soporte.");
        }

        return subscription.Status is SubscriptionStatus.Trial or SubscriptionStatus.Active
            ? LimitCheckResult.Allowed()
            : LimitCheckResult.Denied($"La suscripción de esta organización está {subscription.Status} -- contacta a soporte.");
    }
}
