using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;

namespace PortalSaas.Core.Comercial.Licenciamiento;

public enum LicenseHeartbeatOutcome
{
    Success,
    NotConfigured,
    NoLocalLicense,
    NetworkError,
    RejectedByCentral,
}

public sealed record LicenseHeartbeatResult(LicenseHeartbeatOutcome Outcome, string Message);

/// <summary>
/// Contrato para disparar un heartbeat de licenciamiento bajo demanda -- ver
/// LicenseHeartbeatService para la implementación real (misma lógica que antes vivía
/// solo dentro de LicenseActivatorBackgroundService, extraída acá para que un botón
/// "Validar ahora" del backoffice pueda invocarla sin esperar al intervalo automático).
/// </summary>
public interface ILicenseHeartbeatService
{
    Task<LicenseHeartbeatResult> HeartbeatOnceAsync(CancellationToken ct = default);
}

/// <summary>
/// Lado ON-PREMISE del licenciamiento remoto -- misma lógica que documentaba
/// LicenseActivatorBackgroundService (ver ese archivo, ahora un wrapper delgado sobre
/// esto para el loop automático cada Licensing:HeartbeatIntervalHours). Extraído a un
/// servicio scoped inyectable para que también lo pueda invocar sincrónicamente un
/// botón "Validar licencia manualmente" del backoffice -- útil sobre todo justo
/// después de un alta inicial (Activate.cshtml), en vez de esperar hasta 12h a que el
/// background service corra solo.
/// </summary>
public sealed class LicenseHeartbeatService : ILicenseHeartbeatService
{
    private readonly PortalSaasDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IInstallationFingerprintProvider _fingerprintProvider;
    private readonly IConfiguration _configuration;

    public LicenseHeartbeatService(
        PortalSaasDbContext db,
        IHttpClientFactory httpClientFactory,
        IInstallationFingerprintProvider fingerprintProvider,
        IConfiguration configuration)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _fingerprintProvider = fingerprintProvider;
        _configuration = configuration;
    }

    public async Task<LicenseHeartbeatResult> HeartbeatOnceAsync(CancellationToken ct = default)
    {
        var activationKey = _configuration["Licensing:ActivationKey"];
        var centralServerUrl = _configuration["Licensing:CentralServerUrl"];
        if (string.IsNullOrWhiteSpace(activationKey) || string.IsNullOrWhiteSpace(centralServerUrl))
        {
            return new LicenseHeartbeatResult(
                LicenseHeartbeatOutcome.NotConfigured,
                "Licensing:ActivationKey/Licensing:CentralServerUrl no configurados en esta instalación -- no se puede hacer heartbeat de licencia.");
        }

        var license = await _db.OnPremiseLicenses.FirstOrDefaultAsync(l => l.ActivationKey == activationKey, ct);
        if (license is null)
        {
            return new LicenseHeartbeatResult(
                LicenseHeartbeatOutcome.NoLocalLicense,
                "No existe una licencia local con la ActivationKey configurada en esta instalación.");
        }

        var fingerprint = _fingerprintProvider.GetOrCreate();
        var httpClient = _httpClientFactory.CreateClient();

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(
                $"{centralServerUrl.TrimEnd('/')}/api/licensing/heartbeat",
                new { ActivationKey = activationKey, Fingerprint = fingerprint },
                ct);
        }
        catch (HttpRequestException ex)
        {
            return new LicenseHeartbeatResult(
                LicenseHeartbeatOutcome.NetworkError,
                $"No se pudo contactar al servidor central de licencias ({ex.Message}) -- se sigue con el último token válido hasta que venza la gracia offline.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return new LicenseHeartbeatResult(
                LicenseHeartbeatOutcome.NetworkError,
                $"El servidor central de licencias respondió {response.StatusCode}.");
        }

        var result = await response.Content.ReadFromJsonAsync<HeartbeatResponse>(ct);
        if (result?.SignedToken is not { Length: > 0 })
        {
            return new LicenseHeartbeatResult(
                LicenseHeartbeatOutcome.RejectedByCentral,
                $"El servidor central de licencias rechazó el heartbeat: {result?.Reason ?? "(sin motivo)"}");
        }

        license.SignedStatusToken = result.SignedToken;
        license.SignedStatusUpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new LicenseHeartbeatResult(
            LicenseHeartbeatOutcome.Success,
            result.Reason is { Length: > 0 } ? $"Token actualizado -- {result.Reason}" : "Token actualizado correctamente.");
    }

    private sealed record HeartbeatResponse(bool IsAccepted, string? SignedToken, string? Reason);
}
