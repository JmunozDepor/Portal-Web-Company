using Servicios.Common.Logging;

namespace Servicios.TransferenciaAutomatica_v2;

/// <summary>
/// Corre la purga de retención del log local una vez al día -- deliberadamente separado
/// del Worker de negocio (que corre cada N minutos), mismo criterio que v1.
/// </summary>
public sealed class RetentionPurgeHostedService : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromDays(1);

    private readonly LogRetentionPurger _purger;

    public RetentionPurgeHostedService(LogRetentionPurger purger)
    {
        _purger = purger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _purger.PurgeAsync(stoppingToken);
            await Task.Delay(Intervalo, stoppingToken);
        }
    }
}
