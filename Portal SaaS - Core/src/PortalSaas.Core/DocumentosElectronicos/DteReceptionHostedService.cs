using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;

namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>
/// Cada minuto revisa qué compañías con recepción habilitada ya cumplieron su frecuencia de lectura
/// (ReceptionIntervalMinutes) y las sincroniza con DteReceptionSyncService, un scope por compañía con
/// ICurrentCompanyOverride. Una compañía con error no corta a las demás; sin base al arrancar no tumba el Host.
/// </summary>
public sealed class DteReceptionHostedService : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DteReceptionHostedService> _logger;

    public DteReceptionHostedService(IServiceScopeFactory scopeFactory, ILogger<DteReceptionHostedService> logger)
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
                await SyncDueCompaniesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "No se pudo revisar la recepción de documentos electrónicos.");
            }

            try
            {
                await Task.Delay(Tick, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SyncDueCompaniesAsync(CancellationToken ct)
    {
        List<Guid> due;
        using (var scope = _scopeFactory.CreateScope())
        {
            var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
            var enabled = await scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>().CompanyDteSettings.AsNoTracking()
                .Where(s => s.ReceptionEnabled)
                .Select(s => new { s.CompanyId, s.ReceptionLastSyncAt, s.ReceptionIntervalMinutes })
                .ToListAsync(ct);
            due = enabled
                .Where(s => s.ReceptionLastSyncAt is null || s.ReceptionLastSyncAt.Value.AddMinutes(s.ReceptionIntervalMinutes) <= now)
                .Select(s => s.CompanyId)
                .ToList();
        }

        foreach (var companyId in due)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
                var result = await scope.ServiceProvider.GetRequiredService<DteReceptionSyncService>().SyncCompanyAsync(companyId, ct);
                if (result.New > 0 || result.Updated > 0)
                {
                    _logger.LogInformation("Recepción DTE (compañía {CompanyId}): {New} nuevo(s), {Updated} actualizado(s), {External} respondido(s) fuera del portal.",
                        companyId, result.New, result.Updated, result.MarkedExternal);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("No se pudo sincronizar la recepción DTE de la compañía {CompanyId}: {Error}", companyId, ex.GetType().Name);
            }
        }
    }
}
