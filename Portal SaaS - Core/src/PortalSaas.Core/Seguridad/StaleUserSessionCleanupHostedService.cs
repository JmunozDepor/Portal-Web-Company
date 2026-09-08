using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalSaas.Data;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Borra de verdad las filas muertas de user_sessions -- una fila se crea por cada
/// login y solo se marca IsRevoked en el logout explícito, así que quien cierra el
/// navegador sin desloguearse dejaba la fila para siempre (la tabla se llenaba de
/// "historia" que igual ya no representa a nadie conectado, ver UserSessionService.
/// ListActiveAsync, que las oculta pero no las borra). Corre una vez al arrancar y
/// después cada hora. Borra:
///   - las revocadas (ya no sirven para nada: la cookie se rechaza igual sin la fila),
///   - las más viejas que la vida de la cookie de sesión (si nadie la tocó en 8 h,
///     la cookie ya caducó -- mismo umbral que ListActiveAsync).
/// </summary>
public sealed class StaleUserSessionCleanupHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>Igual que el ExpireTimeSpan de la cookie de tenant (Program.cs) y que UserSessionService.ListActiveAsync.</summary>
    private static readonly TimeSpan SessionCookieLifetime = TimeSpan.FromHours(8);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleUserSessionCleanupHostedService> _logger;

    public StaleUserSessionCleanupHostedService(IServiceScopeFactory scopeFactory, ILogger<StaleUserSessionCleanupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo del ciclo no debe tumbar el host (default de
                // BackgroundServiceExceptionBehavior.StopHost) -- se loguea y se reintenta.
                _logger.LogError(ex, "Fallo al purgar sesiones de portal vencidas");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task<int> PurgeOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();

        var cutoff = DateTimeOffset.UtcNow - SessionCookieLifetime;

        // RemoveRange + SaveChanges (no ExecuteDeleteAsync) -- el volumen de esta tabla
        // es chico (una fila por login de unos pocos usuarios de tenant) y así el
        // borrado funciona igual con cualquier provider EF, tests incluidos.
        var vencidas = await db.UserSessions
            .Where(s => s.IsRevoked || s.CreatedAt < cutoff)
            .ToListAsync(ct);

        if (vencidas.Count == 0)
        {
            return 0;
        }

        db.UserSessions.RemoveRange(vencidas);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Sesiones de portal vencidas purgadas: {Cantidad}", vencidas.Count);
        return vencidas.Count;
    }
}
