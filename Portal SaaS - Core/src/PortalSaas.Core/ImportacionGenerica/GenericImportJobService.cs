using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>Cola en memoria (Singleton) -- el worker la consume de a un trabajo. Ver GenericImportJobWorker.</summary>
public sealed class GenericImportJobQueue : IGenericImportJobQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid jobId) => _channel.Writer.TryWrite(jobId);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>Ver IGenericImportJobService. Acotado a la compañía activa (sesión del usuario).</summary>
public sealed class GenericImportJobService : IGenericImportJobService
{
    internal static readonly JsonSerializerOptions ResultJsonOptions = new(JsonSerializerDefaults.General);

    private readonly PortalSaasDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ICurrentUserContext _currentUser;
    private readonly IGenericImportJobQueue _queue;
    private readonly IGenericImportProgressStore _progress;
    private readonly INotificationService? _notifications;

    /// <summary>Largo máximo del motivo de "Guardar sin enviar" (columna saved_reason).</summary>
    public const int SavedReasonMaxLength = 500;

    public GenericImportJobService(PortalSaasDbContext db, ICurrentCompanyAccessor currentCompany, ICurrentUserContext currentUser,
        IGenericImportJobQueue queue, IGenericImportProgressStore progress, INotificationService? notifications = null)
    {
        _db = db;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _queue = queue;
        _progress = progress;
        _notifications = notifications;
    }

    public async Task<Guid> CreateDraftAsync(GenericImportParametersDto parameters, string fileName, byte[] content, CancellationToken ct = default)
    {
        var job = new GenericImportJob
        {
            CompanyId = _currentCompany.CompanyId,
            UserId = _currentUser.UserId,
            Username = _currentUser.Username,
            Status = GenericImportJobStatus.Draft,
            FileName = string.IsNullOrWhiteSpace(fileName) ? "importacion.xlsx" : Path.GetFileName(fileName)[..Math.Min(Path.GetFileName(fileName).Length, 260)],
            FileHash = Convert.ToHexString(SHA256.HashData(content)),
            FileContent = content,
        };
        ApplyParameters(job, parameters);
        _db.GenericImportJobs.Add(job);
        await _db.SaveChangesAsync(ct);
        return job.Id;
    }

    public async Task UpdateDraftParametersAsync(Guid jobId, GenericImportParametersDto parameters, CancellationToken ct = default)
    {
        var job = await FindAsync(jobId, ct);
        if (job is null || job.Status != GenericImportJobStatus.Draft)
        {
            return;
        }
        ApplyParameters(job, parameters);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<byte[]?> GetFileAsync(Guid jobId, CancellationToken ct = default) =>
        (await FindAsync(jobId, ct))?.FileContent;

    public async Task<GenericImportJobDto?> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _db.GenericImportJobs.AsNoTracking()
            .Where(j => j.Id == jobId && j.CompanyId == _currentCompany.CompanyId)
            .Select(WithoutFile)
            .FirstOrDefaultAsync(ct);
        return job is null ? null : Map(job);
    }

    public async Task<IReadOnlyList<GenericImportJobDto>> ListAsync(int take = 100, CancellationToken ct = default)
    {
        // Los "take" más recientes ya enviados + TODAS las guardadas: una carga guardada
        // antigua no puede desaparecer del listado por quedar fuera del tope.
        var sent = await _db.GenericImportJobs.AsNoTracking()
            .Where(j => j.CompanyId == _currentCompany.CompanyId && j.Status != GenericImportJobStatus.Draft
                && j.Status != GenericImportJobStatus.Saved)
            .OrderByDescending(j => j.CreatedAt)
            .Take(take)
            .Select(WithoutFile)
            .ToListAsync(ct);
        var saved = await _db.GenericImportJobs.AsNoTracking()
            .Where(j => j.CompanyId == _currentCompany.CompanyId && j.Status == GenericImportJobStatus.Saved)
            .Select(WithoutFile)
            .ToListAsync(ct);
        return sent.Concat(saved).OrderByDescending(j => j.CreatedAt).Select(Map).ToList();
    }

    public async Task EnqueueAsync(Guid jobId, GenericImportParametersDto parameters, CancellationToken ct = default)
    {
        var job = await FindAsync(jobId, ct)
            ?? throw new InvalidOperationException("No se encontró el archivo procesado -- volvé a procesar el archivo.");
        if (job.Status is not (GenericImportJobStatus.Draft or GenericImportJobStatus.Saved))
        {
            throw new InvalidOperationException("Este archivo ya fue enviado a SAP -- revisá su estado en Mis importaciones.");
        }

        if (job.FileContent is not { Length: > 0 })
        {
            throw new InvalidOperationException("El archivo de esta carga ya no está disponible -- volvé a subirlo.");
        }

        await EnsureNoOtherJobWithSameFileAsync(job, ct);

        // Carga guardada: se envía tal cual se guardó (sin edición). Borrador temporal:
        // con los parámetros finales del asistente.
        if (job.Status == GenericImportJobStatus.Draft)
        {
            ApplyParameters(job, parameters);
        }

        job.Status = GenericImportJobStatus.Queued;
        job.QueuedAt = DateTimeOffset.UtcNow;
        job.SubmittedByUserId = _currentUser.UserId;
        job.SubmittedByUsername = _currentUser.Username;
        job.ProgressMessage = "En cola -- esperando turno para enviar a SAP.";
        await _db.SaveChangesAsync(ct);

        _queue.Enqueue(job.Id);
    }

    public async Task SaveAsync(Guid jobId, GenericImportParametersDto parameters, string reason, CancellationToken ct = default)
    {
        reason = (reason ?? string.Empty).Trim();
        if (reason.Length == 0)
        {
            throw new InvalidOperationException("Indicá el motivo por el que la carga queda guardada sin enviar.");
        }
        if (reason.Length > SavedReasonMaxLength)
        {
            throw new InvalidOperationException($"El motivo admite hasta {SavedReasonMaxLength} caracteres.");
        }

        var job = await FindAsync(jobId, ct)
            ?? throw new InvalidOperationException("No se encontró el archivo procesado -- volvé a procesar el archivo.");
        if (job.Status != GenericImportJobStatus.Draft)
        {
            throw new InvalidOperationException(job.Status == GenericImportJobStatus.Saved
                ? "Esta carga ya está guardada -- la encontrás en Mis importaciones, pestaña Guardadas."
                : "Este archivo ya fue enviado a SAP -- revisá su estado en Mis importaciones.");
        }

        await EnsureNoOtherJobWithSameFileAsync(job, ct);

        ApplyParameters(job, parameters);
        job.Status = GenericImportJobStatus.Saved;
        job.SavedReason = reason;
        job.SavedAt = DateTimeOffset.UtcNow;
        job.ProgressMessage = "Guardada sin enviar.";
        await _db.SaveChangesAsync(ct);
    }

    public async Task DiscardAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await FindAsync(jobId, ct)
            ?? throw new InvalidOperationException("La carga ya no existe (otro usuario la descartó o venció).");
        if (job.Status != GenericImportJobStatus.Saved)
        {
            throw new InvalidOperationException("Solo se puede descartar una carga guardada sin enviar.");
        }

        _db.GenericImportJobs.Remove(job);
        await _db.SaveChangesAsync(ct);

        if (_notifications is not null && job.UserId != _currentUser.UserId)
        {
            await _notifications.TryEnqueueAsync(job.UserId, "Carga guardada descartada",
                $"{_currentUser.Username} descartó tu carga guardada \"{job.FileName}\" (motivo: {job.SavedReason}).",
                NotificationSeverity.Warning, "/importacion-generica/trabajos?view=guardadas", ct);
        }
    }

    /// <summary>
    /// Control de archivo duplicado (mismo contenido, misma compañía): no se puede enviar ni
    /// guardar si otro trabajo con ese archivo está en cola, ejecutándose o guardado.
    /// </summary>
    private async Task EnsureNoOtherJobWithSameFileAsync(GenericImportJob job, CancellationToken ct)
    {
        var other = await _db.GenericImportJobs.AsNoTracking()
            .Where(j => j.CompanyId == job.CompanyId && j.FileHash == job.FileHash && j.Id != job.Id
                && (j.Status == GenericImportJobStatus.Queued || j.Status == GenericImportJobStatus.Running
                    || j.Status == GenericImportJobStatus.Saved))
            .Select(j => new { j.Status, j.Username })
            .FirstOrDefaultAsync(ct);
        if (other is null)
        {
            return;
        }

        throw new InvalidOperationException(other.Status == GenericImportJobStatus.Saved
            ? $"Este mismo archivo ya está guardado sin enviar (por {other.Username}) -- envialo o descartalo desde Mis importaciones, pestaña Guardadas."
            : "Ya hay una importación en curso con este mismo archivo -- esperá a que termine (Mis importaciones).");
    }

    public async Task<GenericImportProgressDto?> GetProgressAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await GetAsync(jobId, ct);
        if (job is null)
        {
            return null;
        }

        // En vivo (memoria) mientras corre la creación; si no, lo guardado en la base.
        if (job.Status == GenericImportJobStatus.Running && _progress.Get(jobId.ToString()) is { } live && !live.Finished)
        {
            return live;
        }

        return new GenericImportProgressDto(
            job.IsFinished ? job.ResultMessage ?? job.ProgressMessage ?? string.Empty : job.ProgressMessage ?? StatusLabel(job.Status),
            job.ProgressCurrent, job.ProgressTotal,
            job.Status == GenericImportJobStatus.Completed && job.DocumentsFailed == 0 && job.DocumentsSkipped == 0,
            null, job.IsFinished, job.Results);
    }

    internal static string StatusLabel(string status) => status switch
    {
        GenericImportJobStatus.Queued => "En cola",
        GenericImportJobStatus.Running => "En proceso",
        GenericImportJobStatus.Completed => "Terminado",
        GenericImportJobStatus.Failed => "Falló",
        GenericImportJobStatus.Saved => "Guardada sin enviar",
        _ => "Borrador",
    };

    private Task<GenericImportJob?> FindAsync(Guid jobId, CancellationToken ct) =>
        _db.GenericImportJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.CompanyId == _currentCompany.CompanyId, ct);

    private static void ApplyParameters(GenericImportJob job, GenericImportParametersDto parameters)
    {
        job.Module = parameters.Module.ToString();
        job.DocumentType = parameters.DocumentType;
        job.LineType = parameters.LineType.ToString();
        job.BusinessPartnerCardCode = string.IsNullOrWhiteSpace(parameters.BusinessPartnerCardCode) ? null : parameters.BusinessPartnerCardCode.Trim();
        job.ConfigId = parameters.ConfigId;
        job.SalesPersonCode = parameters.SalesPersonCode;
        job.ShipToCode = string.IsNullOrWhiteSpace(parameters.ShipToCode) ? null : parameters.ShipToCode.Trim();
        job.WarehouseCode = string.IsNullOrWhiteSpace(parameters.WarehouseCode) ? null : parameters.WarehouseCode.Trim();
    }

    internal static GenericImportParametersDto ParametersOf(GenericImportJob job) => new(
        Enum.Parse<GenericImportModule>(job.Module), job.DocumentType, Enum.Parse<GenericImportLineType>(job.LineType),
        job.BusinessPartnerCardCode, job.ConfigId, job.SalesPersonCode, job.ShipToCode, job.WarehouseCode);

    // Proyección sin FileContent -- el listado/detalle nunca necesita traer el Excel.
    private static readonly System.Linq.Expressions.Expression<Func<GenericImportJob, GenericImportJob>> WithoutFile = j => new GenericImportJob
    {
        Id = j.Id, CompanyId = j.CompanyId, UserId = j.UserId, Username = j.Username, Status = j.Status,
        SavedReason = j.SavedReason, SavedAt = j.SavedAt, SubmittedByUserId = j.SubmittedByUserId, SubmittedByUsername = j.SubmittedByUsername,
        Module = j.Module, DocumentType = j.DocumentType, LineType = j.LineType, BusinessPartnerCardCode = j.BusinessPartnerCardCode,
        ConfigId = j.ConfigId, SalesPersonCode = j.SalesPersonCode,
        ShipToCode = j.ShipToCode, WarehouseCode = j.WarehouseCode, FileName = j.FileName, FileHash = j.FileHash,
        ProgressCurrent = j.ProgressCurrent, ProgressTotal = j.ProgressTotal, ProgressMessage = j.ProgressMessage,
        ResultMessage = j.ResultMessage, ResultJson = j.ResultJson,
        DocumentsCreated = j.DocumentsCreated, DocumentsFailed = j.DocumentsFailed, DocumentsSkipped = j.DocumentsSkipped,
        CreatedAt = j.CreatedAt, QueuedAt = j.QueuedAt, StartedAt = j.StartedAt, FinishedAt = j.FinishedAt,
    };

    internal static GenericImportJobDto Map(GenericImportJob job) => new()
    {
        Id = job.Id,
        UserId = job.UserId,
        Username = job.Username,
        Status = job.Status,
        SavedReason = job.SavedReason,
        SavedAt = job.SavedAt,
        SubmittedByUsername = job.SubmittedByUsername is { } submitter && submitter != job.Username ? submitter : null,
        Parameters = ParametersOf(job),
        FileName = job.FileName,
        ProgressCurrent = job.ProgressCurrent,
        ProgressTotal = job.ProgressTotal,
        ProgressMessage = job.ProgressMessage,
        ResultMessage = job.ResultMessage,
        Results = string.IsNullOrEmpty(job.ResultJson)
            ? []
            : JsonSerializer.Deserialize<List<GenericImportDocumentResultDto>>(job.ResultJson, ResultJsonOptions) ?? [],
        DocumentsCreated = job.DocumentsCreated,
        DocumentsFailed = job.DocumentsFailed,
        DocumentsSkipped = job.DocumentsSkipped,
        CreatedAt = job.CreatedAt,
        QueuedAt = job.QueuedAt,
        StartedAt = job.StartedAt,
        FinishedAt = job.FinishedAt,
    };
}
