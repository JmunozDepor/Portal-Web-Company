using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Comercial;

/// <summary>Implementación real de IOrganizationAccessGateService.</summary>
public sealed class OrganizationAccessGateService : IOrganizationAccessGateService
{
    private readonly PortalSaasDbContext _db;
    private readonly ILicenseTokenService _tokenService;
    private readonly int _offlineGraceDays;

    public OrganizationAccessGateService(PortalSaasDbContext db, ILicenseTokenService tokenService, IConfiguration configuration)
    {
        _db = db;
        _tokenService = tokenService;
        _offlineGraceDays = configuration.GetValue<int?>("Licensing:OfflineGraceDays") ?? 15;
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

    /// <summary>
    /// Valida el SignedStatusToken firmado por el servidor central, NO las columnas
    /// Status/ExpiresAt en crudo -- esas viven en la BD local de la instalación
    /// on-premise y un backup/restore o una edición manual las puede falsificar sin
    /// dejar rastro. El token sí lo puede, porque su firma solo es válida si la emitió
    /// el central con su clave privada (ver ILicenseTokenService,
    /// LicenseActivatorBackgroundService).
    /// </summary>
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

        if (license.SignedStatusToken is not { Length: > 0 } || license.SignedStatusUpdatedAt is not { } lastUpdate)
        {
            return LimitCheckResult.Denied("Esta instalación todavía no se ha activado contra el servidor central.");
        }

        if (!_tokenService.TryVerify(license.SignedStatusToken, out var payload) || payload is null)
        {
            return LimitCheckResult.Denied("El token de licencia de esta instalación es inválido -- contacta a tu proveedor.");
        }

        if (DateTimeOffset.UtcNow - lastUpdate > TimeSpan.FromDays(_offlineGraceDays))
        {
            return LimitCheckResult.Denied($"Esta instalación no contacta al servidor central hace más de {_offlineGraceDays} días -- verifica la conexión.");
        }

        if (payload.Status != OnPremiseLicenseStatus.Active)
        {
            return LimitCheckResult.Denied($"La licencia de esta instalación está {payload.Status} -- contacta a tu proveedor.");
        }

        if (payload.ExpiresAt <= DateTimeOffset.UtcNow)
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
