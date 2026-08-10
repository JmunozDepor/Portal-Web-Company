using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Comercial.Licenciamiento;

/// <summary>
/// Implementación real de ILicenseActivationService -- corre solo en el rol Central
/// (ver Program.cs, endpoints /api/licensing/*). Opera sobre la copia CENTRAL de
/// `on_premise_licenses` -- la instalación on-premise tiene su propia copia local del
/// mismo esquema, que solo recibe el SignedStatusToken calculado acá.
/// </summary>
public sealed class LicenseActivationService : ILicenseActivationService
{
    private readonly PortalSaasDbContext _db;
    private readonly ILicenseTokenService _tokenService;

    public LicenseActivationService(PortalSaasDbContext db, ILicenseTokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    public async Task<LicenseActivationResult> ActivateOrHeartbeatAsync(string activationKey, string fingerprint, string? sourceIp, CancellationToken ct = default)
    {
        var license = await _db.OnPremiseLicenses
            .Include(l => l.Plan)
            .FirstOrDefaultAsync(l => l.ActivationKey == activationKey, ct);

        if (license is null)
        {
            return LicenseActivationResult.Rejected("Clave de activación desconocida.");
        }

        if (string.IsNullOrEmpty(license.InstallationFingerprint))
        {
            // Primera activación -- esta instalación gana el fingerprint.
            license.InstallationFingerprint = fingerprint;
        }
        else if (license.InstallationFingerprint != fingerprint)
        {
            _db.OnPremiseLicenseConflicts.Add(new OnPremiseLicenseConflict
            {
                OnPremiseLicenseId = license.Id,
                ReportedFingerprint = fingerprint,
                ReportedIp = sourceIp,
            });
            await _db.SaveChangesAsync(ct);

            return LicenseActivationResult.Rejected(
                "Esta clave de activación ya está vinculada a otra instalación. El conflicto quedó registrado para revisión manual.");
        }

        // Se firma y entrega el estado tal cual está hoy -- incluido revoked/expired,
        // porque el on-premise necesita PRUEBA firmada del rechazo, no solo la
        // ausencia de un token válido (que también podría deberse a un corte de red).
        var payload = new LicenseStatusPayload(
            license.OrganizationId,
            license.PlanId,
            license.Plan.UserLimit,
            license.Plan.CompanyLimit,
            license.Plan.MonthlyTransactionLimit,
            license.Status,
            license.ExpiresAt,
            DateTimeOffset.UtcNow);

        var signedToken = _tokenService.Sign(payload);

        license.SignedStatusToken = signedToken;
        license.SignedStatusUpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return license.Status == OnPremiseLicenseStatus.Active && license.ExpiresAt > DateTimeOffset.UtcNow
            ? LicenseActivationResult.Accepted(signedToken)
            : LicenseActivationResult.Accepted(signedToken, $"Licencia en estado '{license.Status}'.");
    }
}
