using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;

namespace PortalSaas.Core.Documentos;

/// <summary>
/// Limpieza de borradores de documentos según la retención de cada compañía
/// (company_document_settings.draft_retention_days; sin fila o null = no vencen). Corre al
/// arrancar y cada 6 horas. Borra pendientes y enviados sin modificar desde hace más de N
/// días; nunca uno que se esté enviando. Sin base al arrancar no tumba el Host.
/// </summary>
public sealed class DocumentDraftCleanupWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentDraftCleanupWorker> _logger;

    public DocumentDraftCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<DocumentDraftCleanupWorker> logger)
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
                using var scope = _scopeFactory.CreateScope();
                var removed = await CleanAsync(scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>(), DateTimeOffset.UtcNow, stoppingToken);
                if (removed > 0)
                {
                    _logger.LogInformation("Limpieza de borradores de documentos: {Count} eliminado(s) por retención.", removed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "No se pudo ejecutar la limpieza de borradores de documentos.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Expuesto para tests: elimina los borradores vencidos según la retención de cada compañía.</summary>
    internal static async Task<int> CleanAsync(PortalSaasDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var retentions = await db.CompanyDocumentSettings.AsNoTracking()
            .Where(s => s.DraftRetentionDays != null && s.DraftRetentionDays > 0)
            .Select(s => new { s.CompanyId, Days = s.DraftRetentionDays!.Value })
            .ToListAsync(ct);

        var removed = 0;
        foreach (var retention in retentions)
        {
            var limit = now.AddDays(-retention.Days);
            var expired = await db.DocumentDrafts
                .Where(d => d.CompanyId == retention.CompanyId && d.Status != DocumentDraftStatus.Sending && d.UpdatedAt < limit)
                .ToListAsync(ct);
            db.DocumentDrafts.RemoveRange(expired);
            removed += expired.Count;
        }

        if (removed > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return removed;
    }
}
