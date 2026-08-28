using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Comercial;

/// <summary>
/// Implementación real de IModuleAccessService. Resolución del plan activo por modo
/// -- copia deliberada, a propósito más chica, de ContractLimitService.GetActivePlanAsync
/// (acá solo hace falta el PlanId, no el Plan completo con sus límites) -- mismo
/// criterio de "implementaciones paralelas, no una superclase compartida" que ya rige
/// los 3 motores genéricos de documento (ver CLAUDE.md).
/// </summary>
public sealed class ModuleAccessService : IModuleAccessService
{
    private readonly PortalSaasDbContext _db;

    public ModuleAccessService(PortalSaasDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlySet<string>> GetCatalogedModuleCodesAsync(CancellationToken ct = default)
    {
        var codes = await _db.PlatformModules.Select(m => m.Code).ToListAsync(ct);
        return codes.ToHashSet();
    }

    public async Task<IReadOnlySet<string>> GetContractedModuleCodesAsync(Guid organizationId, CancellationToken ct = default)
    {
        var coreCodes = await _db.PlatformModules
            .Where(m => m.IsCore)
            .Select(m => m.Code)
            .ToListAsync(ct);

        var planId = await ResolveActivePlanIdAsync(organizationId, ct);
        var planCodes = planId is null
            ? []
            : await _db.PlanModules
                .Where(pm => pm.PlanId == planId)
                .Select(pm => pm.Module.Code)
                .ToListAsync(ct);

        var addonCodes = await _db.OrganizationModules
            .Where(om => om.OrganizationId == organizationId)
            .Select(om => om.Module.Code)
            .ToListAsync(ct);

        // Un módulo exclusivo de OTRA organización nunca queda contratado acá, sin
        // importar que se haya colado en un Plan/OrganizationModule por error -- es la
        // garantía real detrás de "aplicación puntual de un solo cliente".
        var exclusiveElsewhereCodes = await _db.PlatformModules
            .Where(m => m.ExclusiveOrganizationId != null && m.ExclusiveOrganizationId != organizationId)
            .Select(m => m.Code)
            .ToListAsync(ct);

        return coreCodes.Concat(planCodes).Concat(addonCodes)
            .Except(exclusiveElsewhereCodes)
            .ToHashSet();
    }

    public async Task<IReadOnlySet<string>> GetHiddenModuleCodesAsync(Guid organizationId, CancellationToken ct = default)
    {
        var codes = await _db.OrganizationModuleVisibilities
            .Where(v => v.OrganizationId == organizationId && v.IsHidden)
            .Select(v => v.Module.Code)
            .ToListAsync(ct);
        return codes.ToHashSet();
    }

    private async Task<long?> ResolveActivePlanIdAsync(Guid organizationId, CancellationToken ct)
    {
        var organization = await _db.Organizations.FindAsync([organizationId], ct);
        if (organization is null)
        {
            return null;
        }

        if (organization.Mode == OrganizationMode.OnPremise)
        {
            var now = DateTimeOffset.UtcNow;
            var license = await _db.OnPremiseLicenses
                .Where(l => l.OrganizationId == organizationId && l.Status == OnPremiseLicenseStatus.Active && l.ExpiresAt > now)
                .OrderByDescending(l => l.ExpiresAt)
                .FirstOrDefaultAsync(ct);

            return license?.PlanId;
        }

        var subscription = await _db.Subscriptions
            .Where(s => s.OrganizationId == organizationId && (s.Status == SubscriptionStatus.Trial || s.Status == SubscriptionStatus.Active))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

        return subscription?.PlanId;
    }
}
