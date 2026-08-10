using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;

namespace PortalSaas.Core.Comercial.Licenciamiento;

/// <summary>
/// Lado ON-PREMISE del licenciamiento remoto -- registrado en Program.cs solo cuando
/// Licensing:Role = "OnPremise" (ver plan). Al arrancar y luego cada
/// Licensing:HeartbeatIntervalHours (default 12h), llama a
/// {Licensing:CentralServerUrl}/api/licensing/heartbeat con la ActivationKey local y el
/// InstallationFingerprint de esta máquina (IInstallationFingerprintProvider), y guarda
/// el token firmado que responda el central en la fila local de OnPremiseLicenses --
/// CheckLicenseAsync es quien valida ESE token, no las columnas Status/ExpiresAt
/// crudas (ver OrganizationAccessGateService).
///
/// Sin conexión: no hace nada y deja pasar el intervalo -- CheckLicenseAsync decide
/// con la gracia offline (Licensing:OfflineGraceDays) si la instalación sigue
/// funcionando con el último token válido o si ya venció la gracia.
/// </summary>
public sealed class LicenseActivatorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IInstallationFingerprintProvider _fingerprintProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LicenseActivatorBackgroundService> _logger;

    public LicenseActivatorBackgroundService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IInstallationFingerprintProvider fingerprintProvider,
        IConfiguration configuration,
        ILogger<LicenseActivatorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _fingerprintProvider = fingerprintProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = _configuration.GetValue<double?>("Licensing:HeartbeatIntervalHours") ?? 12;
        var interval = TimeSpan.FromHours(intervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            await HeartbeatOnceAsync(stoppingToken);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task HeartbeatOnceAsync(CancellationToken ct)
    {
        var activationKey = _configuration["Licensing:ActivationKey"];
        var centralServerUrl = _configuration["Licensing:CentralServerUrl"];
        if (string.IsNullOrWhiteSpace(activationKey) || string.IsNullOrWhiteSpace(centralServerUrl))
        {
            _logger.LogWarning("Licensing:ActivationKey/Licensing:CentralServerUrl no configurados -- no se puede hacer heartbeat de licencia.");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();

        var license = await db.OnPremiseLicenses.FirstOrDefaultAsync(l => l.ActivationKey == activationKey, ct);
        if (license is null)
        {
            _logger.LogWarning("No existe una OnPremiseLicense local con la ActivationKey configurada -- ¿falta el alta manual inicial?");
            return;
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
            _logger.LogWarning(ex, "No se pudo contactar al servidor central de licencias -- se sigue con el último token válido hasta que venza la gracia offline.");
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("El servidor central de licencias respondió {StatusCode}.", response.StatusCode);
            return;
        }

        var result = await response.Content.ReadFromJsonAsync<HeartbeatResponse>(ct);
        if (result?.SignedToken is not { Length: > 0 })
        {
            _logger.LogWarning("El servidor central de licencias rechazó el heartbeat: {Reason}", result?.Reason ?? "(sin motivo)");
            return;
        }

        license.SignedStatusToken = result.SignedToken;
        license.SignedStatusUpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private sealed record HeartbeatResponse(bool IsAccepted, string? SignedToken, string? Reason);
}
