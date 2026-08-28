using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PortalSaas.Core.Comercial.Licenciamiento;

/// <summary>
/// Lado ON-PREMISE del licenciamiento remoto -- registrado en Program.cs solo cuando
/// Licensing:Role = "OnPremise". Al arrancar y luego cada Licensing:HeartbeatIntervalHours
/// (default 12h), corre ILicenseHeartbeatService.HeartbeatOnceAsync (ver esa clase para
/// el protocolo completo: llamada a {Licensing:CentralServerUrl}/api/licensing/heartbeat,
/// guardado del token firmado en la fila local de OnPremiseLicenses). Este archivo es
/// solo el loop periódico -- la lógica real vive en LicenseHeartbeatService para que
/// también la pueda invocar sincrónicamente un botón "Validar ahora" del backoffice
/// (ver Pages/Admin/Organizations/Licenses/Index.cshtml.cs).
///
/// Sin conexión: no hace nada y deja pasar el intervalo -- CheckLicenseAsync decide
/// con la gracia offline (Licensing:OfflineGraceDays) si la instalación sigue
/// funcionando con el último token válido o si ya venció la gracia.
/// </summary>
public sealed class LicenseActivatorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LicenseActivatorBackgroundService> _logger;

    public LicenseActivatorBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<LicenseActivatorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = _configuration.GetValue<double?>("Licensing:HeartbeatIntervalHours") ?? 12;
        var interval = TimeSpan.FromHours(intervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var heartbeatService = scope.ServiceProvider.GetRequiredService<ILicenseHeartbeatService>();
                var result = await heartbeatService.HeartbeatOnceAsync(stoppingToken);
                if (result.Outcome != LicenseHeartbeatOutcome.Success)
                {
                    _logger.LogWarning("Heartbeat de licencia: {Outcome} -- {Message}", result.Outcome, result.Message);
                }
            }

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
}
