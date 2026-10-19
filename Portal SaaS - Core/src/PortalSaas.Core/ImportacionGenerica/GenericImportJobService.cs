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

    public GenericImportJobService(PortalSaasDbContext db, ICurrentCompanyAccessor currentCompany, ICurrentUserContext currentUser,
        IGenericImportJobQueue queue, IGenericImportProgressStore progress)
    {
        _db = db;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _queue = queue;
        _progress = progress;
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
        var jobs = await _db.GenericImportJobs.AsNoTracking()
            .Where(j => j.CompanyId == _currentCompany.CompanyId && j.Status != GenericImportJobStatus.Draft)
            .OrderByDescending(j => j.CreatedAt)
            .Take(take)
            .Select(WithoutFile)
            .ToListAsync(ct);
        return jobs.Select(Map).ToList();
    }

    public async Task EnqueueAsync(Guid jobId, GenericImportParametersDto parameters, CancellationToken ct = default)
    {
        var job = await FindAsync(jobId, ct)
            ?? throw new InvalidOperationException("No se encontró el archivo procesado -- volvé a procesar el archivo.");
        if (job.Status != GenericImportJobStatus.Draft)
        {
            throw new InvalidOperationException("Este archivo ya fue enviado a SAP -- revisá su estado en Mis importaciones.");
        }

        // Doble envío: el mismo archivo (mismo contenido) ya está en cola o ejecutándose.
        var running = await _db.GenericImportJobs.AnyAsync(j => j.CompanyId == job.CompanyId && j.FileHash == job.FileHash
            && j.Id != job.Id && (j.Status == GenericImportJobStatus.Queued || j.Status == GenericImportJobStatus.Running), ct);
        if (running)
        {
            throw new InvalidOperationException("Ya hay una importación en curso con este mismo archivo -- esperá a que termine (Mis importaciones).");
        }

        ApplyParameters(job, parameters);
        job.Status = GenericImportJobStatus.Queued;
        job.QueuedAt = DateTimeOffset.UtcNow;
        job.ProgressMessage = "En cola -- esperando turno para enviar a SAP.";
        await _db.SaveChangesAsync(ct);

        _queue.Enqueue(job.Id);
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
    }

    internal static GenericImportParametersDto ParametersOf(GenericImportJob job) => new(
        Enum.Parse<GenericImportModule>(job.Module), job.DocumentType, Enum.Parse<GenericImportLineType>(job.LineType),
        job.BusinessPartnerCardCode, job.ConfigId, job.SalesPersonCode);

    // Proyección sin FileContent -- el listado/detalle nunca necesita traer el Excel.
    private static readonly System.Linq.Expressions.Expression<Func<GenericImportJob, GenericImportJob>> WithoutFile = j => new GenericImportJob
    {
        Id = j.Id, CompanyId = j.CompanyId, UserId = j.UserId, Username = j.Username, Status = j.Status,
        Module = j.Module, DocumentType = j.DocumentType, LineType = j.LineType, BusinessPartnerCardCode = j.BusinessPartnerCardCode,
        ConfigId = j.ConfigId, SalesPersonCode = j.SalesPersonCode, FileName = j.FileName, FileHash = j.FileHash,
        ProgressCurrent = j.ProgressCurrent, ProgressTotal = j.ProgressTotal, ProgressMessage = j.ProgressMessage,
        ResultMessage = j.ResultMessage, ResultJson = j.ResultJson,
        DocumentsCreated = j.DocumentsCreated, DocumentsFailed = j.DocumentsFailed, DocumentsSkipped = j.DocumentsSkipped,
        CreatedAt = j.CreatedAt, QueuedAt = j.QueuedAt, StartedAt = j.StartedAt, FinishedAt = j.FinishedAt,
    };

    internal static GenericImportJobDto Map(GenericImportJob job) => new()
    {
        Id = job.Id,
        Username = job.Username,
        Status = job.Status,
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
