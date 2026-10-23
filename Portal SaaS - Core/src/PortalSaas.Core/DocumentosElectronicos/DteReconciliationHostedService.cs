using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Data;

namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>
/// Cada 5 min (y al arrancar): retoma las emisiones DTE que quedaron a medias más de 10 min
/// (pending sin respuesta del proveedor, folio_reserved sin confirmar) con el mismo camino
/// idempotente de DteEmissionService.AdvanceAsync. Nunca toca "failed": un rechazo de negocio lo
/// corrige el módulo de origen. Un scope por compañía con ICurrentCompanyOverride, así cada
/// emisión usa la configuración DTE de SU compañía. Sin base al arrancar no tumba el Host.
/// </summary>
public sealed class DteReconciliationHostedService : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan Stale = TimeSpan.FromMinutes(10);
    internal const int MaxAttempts = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DteReconciliationHostedService> _logger;

    public DteReconciliationHostedService(IServiceScopeFactory scopeFactory, ILogger<DteReconciliationHostedService> logger)
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
                await ReconcileAllAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "No se pudo conciliar las emisiones DTE pendientes.");
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

    private async Task ReconcileAllAsync(DateTimeOffset now, CancellationToken ct)
    {
        List<Guid> companies;
        using (var scope = _scopeFactory.CreateScope())
        {
            companies = await StaleQuery(scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>(), null, now)
                .Select(e => e.CompanyId).Distinct().ToListAsync(ct);
        }

        foreach (var companyId in companies)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
                var advanced = await ReconcileCompanyAsync(
                    scope.ServiceProvider.GetRequiredService<DteEmissionService>(),
                    scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>(),
                    companyId, now, _logger, ct);
                if (advanced > 0)
                {
                    _logger.LogInformation("Conciliación DTE (compañía {CompanyId}): {Count} emisión(es) retomada(s).", companyId, advanced);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Sin detalles del proveedor: el mensaje de excepción nunca lleva credenciales, pero tampoco hace falta.
                _logger.LogError("No se pudo conciliar las emisiones DTE de la compañía {CompanyId}: {Error}", companyId, ex.GetType().Name);
            }
        }
    }

    /// <summary>Expuesto para tests: avanza las emisiones vencidas de una compañía. Devuelve cuántas se intentaron.</summary>
    internal static async Task<int> ReconcileCompanyAsync(
        DteEmissionService svc, PortalSaasDbContext db, Guid companyId, DateTimeOffset now, ILogger logger, CancellationToken ct)
    {
        var rows = await StaleQuery(db, companyId, now).ToListAsync(ct);
        foreach (var row in rows)
        {
            var result = await svc.AdvanceAsync(row, ct);
            if (result.Status != DteEmissionStatuses.Issued)
            {
                logger.LogWarning("Emisión DTE {EmissionKey} ({SourceRef}) sigue en {Status}.", row.EmissionKey, row.SourceRef, result.Status);
            }
        }
        return rows.Count;
    }

    private static IQueryable<Data.Entities.DteEmission> StaleQuery(PortalSaasDbContext db, Guid? companyId, DateTimeOffset now)
    {
        var limit = now - Stale;
        var q = db.DteEmissions.Where(e =>
            (e.Status == DteEmissionStatuses.Pending || e.Status == DteEmissionStatuses.FolioReserved)
            && e.UpdatedAt < limit && e.Attempts < MaxAttempts);
        return companyId is { } id ? q.Where(e => e.CompanyId == id) : q;
    }
}
