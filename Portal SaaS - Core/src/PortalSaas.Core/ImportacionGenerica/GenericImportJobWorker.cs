using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// Procesa en segundo plano los trabajos de Importación Genérica (2026-10-15), de a UNO
/// por vez (para no saturar Service Layer) y con su propio ciclo de vida: la creación en
/// SAP ya no depende de la petición HTTP del usuario, así que cerrar la pestaña, perder la
/// conexión o un timeout del proxy no la cortan a mitad.
///
/// Sin HttpContext: la compañía se fija con ICurrentCompanyOverride (mismo mecanismo que
/// IntegrationSyncHostedService). Dos scopes por trabajo: uno para el trabajo SAP y otro
/// solo para ir guardando el progreso del registro -- un DbContext no se puede usar desde
/// dos tareas a la vez.
///
/// Al arrancar: los trabajos que quedaron "Running" (reinicio del Host a mitad) pasan a
/// "Saved" (guardados) y se avisa por la campana -- NO se reintentan solos; al reenviarlos,
/// la marca de origen de cada documento (IDocumentSapMarkerService) evita duplicar los que
/// ya se crearon (2026-10-21; antes quedaban fallidos y había que revisar SAP a mano). Los
/// "Queued" se vuelven a encolar. Además limpia borradores temporales viejos y el Excel de
/// trabajos antiguos. Los "Saved" los vence DocumentDraftCleanupWorker (retención por compañía).
/// </summary>
public sealed class GenericImportJobWorker : BackgroundService
{
    private static readonly TimeSpan DraftRetention = TimeSpan.FromDays(1);
    private static readonly TimeSpan FileRetention = TimeSpan.FromDays(30);
    private static readonly TimeSpan ProgressPersistInterval = TimeSpan.FromSeconds(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGenericImportJobQueue _queue;
    private readonly IGenericImportProgressStore _progress;
    private readonly ILogger<GenericImportJobWorker> _logger;

    public GenericImportJobWorker(IServiceScopeFactory scopeFactory, IGenericImportJobQueue queue, IGenericImportProgressStore progress,
        ILogger<GenericImportJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _progress = progress;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverAndCleanAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin base al arrancar (ej. VPN caída) no debe tumbar el Host -- los trabajos
            // nuevos igual se procesan cuando se encolen.
            _logger.LogError(ex, "No se pudo recuperar/limpiar los trabajos de importación al iniciar.");
        }

        await foreach (var jobId in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error no controlado procesando el trabajo de importación {JobId}.", jobId);
            }
        }
    }

    private async Task RecoverAndCleanAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var now = DateTimeOffset.UtcNow;

        // Interrumpidos: con el archivo disponible pasan a "Guardada" para reenviarlos -- cada
        // documento lleva la marca de origen (IDocumentSapMarkerService), así que al
        // reenviar los que ya alcanzaron a crearse se reconocen y NO se duplican. Sin
        // archivo (no debería pasar) quedan fallidos como antes.
        var interruptedJobs = await db.GenericImportJobs.Where(j => j.Status == GenericImportJobStatus.Running).ToListAsync(ct);
        foreach (var interrupted in interruptedJobs)
        {
            if (interrupted.FileContent is { Length: > 0 })
            {
                interrupted.Status = GenericImportJobStatus.Saved;
                interrupted.SavedAt = now;
                interrupted.SavedReason = InterruptedReason;
                interrupted.ProgressMessage = "Interrumpida por un reinicio del servidor -- guardada para reenviar.";
            }
            else
            {
                interrupted.Status = GenericImportJobStatus.Failed;
                interrupted.FinishedAt = now;
                interrupted.ResultMessage = "Interrumpido por un reinicio del servidor -- algunos pedidos pueden haber quedado creados. " +
                    "Revisá en SAP antes de volver a cargar (la regla \"OC ya cargada en SAP\" lo detecta).";
            }
        }

        var queued = await db.GenericImportJobs.Where(j => j.Status == GenericImportJobStatus.Queued)
            .OrderBy(j => j.QueuedAt).Select(j => j.Id).ToListAsync(ct);

        var draftLimit = now - DraftRetention;
        db.GenericImportJobs.RemoveRange(await db.GenericImportJobs
            .Where(j => j.Status == GenericImportJobStatus.Draft && j.CreatedAt < draftLimit).ToListAsync(ct));

        var fileLimit = now - FileRetention;
        foreach (var old in await db.GenericImportJobs
                     .Where(j => (j.Status == GenericImportJobStatus.Completed || j.Status == GenericImportJobStatus.Failed)
                         && j.FinishedAt < fileLimit && j.FileContent != null).ToListAsync(ct))
        {
            old.FileContent = null;
        }

        await db.SaveChangesAsync(ct);

        var notifications = scope.ServiceProvider.GetService<INotificationService>();
        foreach (var interrupted in interruptedJobs.Where(j => j.Status == GenericImportJobStatus.Saved))
        {
            if (notifications is not null)
            {
                await notifications.TryEnqueueAsync(interrupted.SubmittedByUserId ?? interrupted.UserId, "Importación interrumpida",
                    $"{interrupted.FileName}: el envío a SAP se cortó por un reinicio del servidor. Quedó guardada -- reenviala; " +
                    "los documentos que ya se crearon no se duplican.",
                    NotificationSeverity.Warning, $"/importacion-generica/trabajos/{interrupted.Id}", CancellationToken.None);
            }
        }

        foreach (var jobId in queued)
        {
            _queue.Enqueue(jobId);
        }
    }

    internal const string InterruptedReason =
        "Envío interrumpido por un reinicio del servidor. Al reenviar, los documentos que ya se alcanzaron a crear en SAP " +
        "se reconocen por su marca de origen y no se duplican.";

    private async Task ProcessJobAsync(Guid jobId, CancellationToken ct)
    {
        // Scope del REGISTRO del trabajo (estado/progreso).
        using var jobScope = _scopeFactory.CreateScope();
        var jobDb = jobScope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var job = await jobDb.GenericImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.Status != GenericImportJobStatus.Queued)
        {
            return;
        }

        job.Status = GenericImportJobStatus.Running;
        job.StartedAt = DateTimeOffset.UtcNow;
        job.ProgressMessage = "Validando el archivo contra SAP...";
        await jobDb.SaveChangesAsync(ct);

        var parameters = GenericImportJobService.ParametersOf(job);
        var progressKey = jobId.ToString();

        try
        {
            if (job.FileContent is not { Length: > 0 } content)
            {
                throw new InvalidOperationException("El trabajo no tiene archivo.");
            }

            // Scope del TRABAJO SAP, con la compañía del trabajo fijada (sin HttpContext).
            using var workScope = _scopeFactory.CreateScope();
            workScope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(job.CompanyId);
            var importService = workScope.ServiceProvider.GetRequiredService<IGenericImportService>();

            using var stream = new MemoryStream(content);
            var preview = await importService.ProcessFileAsync(parameters, stream, ct);
            if (!preview.HasValidConfig)
            {
                throw new InvalidOperationException(preview.ErrorMessage ?? "El archivo no se pudo procesar con este formato.");
            }

            // Firma de los documentos (U_PortalUser): quien envía -- una carga guardada la
            // puede enviar otro usuario con permiso.
            var creation = importService.CreateDocumentsAsync(progressKey, job.SubmittedByUsername ?? job.Username, parameters, preview.Documents, ct);
            while (!creation.IsCompleted)
            {
                await Task.WhenAny(creation, Task.Delay(ProgressPersistInterval, ct));
                if (_progress.Get(progressKey) is { } live)
                {
                    job.ProgressCurrent = live.Current;
                    job.ProgressTotal = live.Total;
                    job.ProgressMessage = Truncate(live.Message, 500);
                    await jobDb.SaveChangesAsync(ct);
                }
            }

            var result = await creation;
            var results = result.Results ?? [];
            job.Status = GenericImportJobStatus.Completed;
            job.ProgressCurrent = result.Current;
            job.ProgressTotal = result.Total;
            job.ProgressMessage = Truncate(result.Message, 500);
            job.ResultMessage = Truncate(result.Message, 2000);
            job.ResultJson = JsonSerializer.Serialize(results, GenericImportJobService.ResultJsonOptions);
            job.DocumentsCreated = results.Count(r => r.Status == "Created");
            job.DocumentsFailed = results.Count(r => r.Status == "SapError");
            job.DocumentsSkipped = results.Count(r => r.Status == "Skipped");
            job.FinishedAt = DateTimeOffset.UtcNow;
            await jobDb.SaveChangesAsync(CancellationToken.None);

            await NotifyAsync(jobScope, job, job.DocumentsFailed == 0 && job.DocumentsSkipped == 0
                ? NotificationSeverity.Success
                : job.DocumentsCreated > 0 ? NotificationSeverity.Warning : NotificationSeverity.Danger);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Apagado del Host: queda "Running" y RecoverAndCleanAsync lo marca al reiniciar.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló el trabajo de importación {JobId}.", jobId);
            job.Status = GenericImportJobStatus.Failed;
            job.ResultMessage = Truncate($"La importación falló: {ex.Message}", 2000);
            job.ProgressMessage = Truncate(job.ResultMessage, 500);
            job.FinishedAt = DateTimeOffset.UtcNow;
            await jobDb.SaveChangesAsync(CancellationToken.None);
            await NotifyAsync(jobScope, job, NotificationSeverity.Danger);
        }
        finally
        {
            _progress.Remove(progressKey);
        }
    }

    private static async Task NotifyAsync(IServiceScope scope, GenericImportJob job, string severity)
    {
        var notifications = scope.ServiceProvider.GetService<INotificationService>();
        if (notifications is null)
        {
            return;
        }
        await notifications.TryEnqueueAsync(job.UserId, "Importación genérica",
            $"{job.FileName}: {job.ResultMessage}", severity, $"/importacion-generica/trabajos/{job.Id}", CancellationToken.None);

        // Carga guardada enviada por otro usuario: también le avisa a quien la envió.
        if (job.SubmittedByUserId is { } submitter && submitter != job.UserId)
        {
            await notifications.TryEnqueueAsync(submitter, "Importación genérica",
                $"{job.FileName}: {job.ResultMessage}", severity, $"/importacion-generica/trabajos/{job.Id}", CancellationToken.None);
        }
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
