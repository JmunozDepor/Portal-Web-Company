using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;

namespace PortalSaas.Core.Documentos;

/// <summary>
/// Mantenimiento de borradores de documentos, en segundo plano:
/// - Cada 15 min (y al arrancar): CONCILIACIÓN de los borradores que quedaron "Enviando"
///   más de 10 min (servidor caído o timeout a mitad del envío). Busca en SAP la marca de
///   origen del borrador (IDocumentSapMarkerService): si el documento existe, el borrador
///   pasa a "Enviado" con su DocEntry; si no, a "Con error" para reintentar. Avisa al
///   creador por la campana. Mismo criterio que GenericImportJobWorker con sus trabajos
///   interrumpidos, pero resolviendo solo (sin revisar SAP a mano). SAP caído: se reintenta
///   en la vuelta siguiente.
/// - Cada 6 horas: LIMPIEZA según la retención de cada compañía
///   (company_document_settings.draft_retention_days; sin fila o null = no vencen), de los
///   borradores digitados y de las importaciones genéricas guardadas sin enviar.
/// Sin base al arrancar no tumba el Host.
/// </summary>
public sealed class DocumentDraftCleanupWorker : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);
    internal const string SystemUsername = "sistema";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentDraftCleanupWorker> _logger;

    public DocumentDraftCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<DocumentDraftCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            try
            {
                await ReconcileAllAsync(now, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "No se pudo conciliar los borradores de documentos interrumpidos.");
            }

            if (now - lastCleanup >= CleanupInterval)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var removed = await CleanAsync(scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>(), now, stoppingToken);
                    if (removed > 0)
                    {
                        _logger.LogInformation("Limpieza de borradores de documentos: {Count} eliminado(s) por retención.", removed);
                    }

                    lastCleanup = now;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "No se pudo ejecutar la limpieza de borradores de documentos.");
                }
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
            var limit = now - DocumentDraftService.StaleSending;
            companies = await scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>().DocumentDrafts.AsNoTracking()
                .Where(d => d.Status == DocumentDraftStatus.Sending && d.UpdatedAt < limit)
                .Select(d => d.CompanyId)
                .Distinct()
                .ToListAsync(ct);
        }

        foreach (var companyId in companies)
        {
            // Scope por compañía con la compañía fijada (sin HttpContext) -- mismo mecanismo
            // que GenericImportJobWorker, así el marcador consulta el SAP correcto.
            using var scope = _scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
            var (sent, failed) = await ReconcileCompanyAsync(
                scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>(),
                scope.ServiceProvider.GetRequiredService<IDocumentSapMarkerService>(),
                scope.ServiceProvider.GetRequiredService<INotificationService>(),
                companyId, now, _logger, ct);
            if (sent + failed > 0)
            {
                _logger.LogInformation("Conciliación de borradores (compañía {CompanyId}): {Sent} ya estaban en SAP, {Failed} no llegaron.", companyId, sent, failed);
            }
        }
    }

    /// <summary>Expuesto para tests: concilia los borradores "Enviando" vencidos de una compañía contra SAP.</summary>
    internal static async Task<(int Sent, int Failed)> ReconcileCompanyAsync(PortalSaasDbContext db, IDocumentSapMarkerService markers,
        INotificationService notifications, Guid companyId, DateTimeOffset now, ILogger logger, CancellationToken ct)
    {
        var limit = now - DocumentDraftService.StaleSending;
        var drafts = await db.DocumentDrafts
            .Where(d => d.CompanyId == companyId && d.Status == DocumentDraftStatus.Sending && d.UpdatedAt < limit)
            .ToListAsync(ct);

        int sent = 0, failed = 0;
        foreach (var draft in drafts)
        {
            int? docEntry;
            try
            {
                docEntry = await markers.FindDocEntryAsync(draft.Engine, draft.DocumentType, draft.Id.ToString("N"), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // SAP no disponible: sin certeza, no se toca -- se reintenta en la vuelta siguiente.
                logger.LogWarning(ex, "No se pudo verificar en SAP el borrador {DraftId}; se reintenta más tarde.", draft.Id);
                continue;
            }

            draft.UpdatedByUsername = SystemUsername;
            draft.UpdatedAt = now;
            draft.Version++;
            if (docEntry is { } entry)
            {
                draft.Status = DocumentDraftStatus.Sent;
                draft.SapDocEntry = entry;
                draft.SentAt = now;
                draft.ErrorMessage = null;
                sent++;
            }
            else
            {
                draft.Status = DocumentDraftStatus.Error;
                draft.ErrorMessage = "Envío interrumpido: el documento no llegó a SAP. Podés reintentar el envío.";
                failed++;
            }

            await db.SaveChangesAsync(ct);

            if (docEntry is { } created)
            {
                await notifications.TryEnqueueAsync(draft.CreatedByUserId, "Borrador conciliado",
                    $"Tu borrador ({draft.PartnerCode ?? "sin socio"} · {draft.LineCount} línea(s)) ya estaba creado en SAP -- DocEntry {created}. No se duplicó.",
                    NotificationSeverity.Success, null, ct);
            }
            else
            {
                await notifications.TryEnqueueAsync(draft.CreatedByUserId, "Envío de borrador interrumpido",
                    $"Tu borrador ({draft.PartnerCode ?? "sin socio"} · {draft.LineCount} línea(s)) no llegó a SAP por una interrupción. Abrilo y volvé a enviarlo.",
                    NotificationSeverity.Warning, null, ct);
            }
        }

        return (sent, failed);
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

            // Importación Genérica guardada sin enviar (borradores etapa 2): misma retención.
            var expiredImports = await db.GenericImportJobs
                .Where(j => j.CompanyId == retention.CompanyId && j.Status == GenericImportJobStatus.Saved && j.SavedAt < limit)
                .ToListAsync(ct);
            db.GenericImportJobs.RemoveRange(expiredImports);
            removed += expiredImports.Count;
        }

        if (removed > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return removed;
    }
}
