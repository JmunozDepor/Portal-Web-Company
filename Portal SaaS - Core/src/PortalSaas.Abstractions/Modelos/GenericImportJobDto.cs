namespace PortalSaas.Abstractions.Modelos;

/// <summary>Estados de un trabajo de importación -- ver GenericImportJob (Data).</summary>
public static class GenericImportJobStatus
{
    public const string Draft = "Draft";
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

/// <summary>Un trabajo de importación (sin el archivo) -- "Mis importaciones" y su detalle.</summary>
public sealed record GenericImportJobDto
{
    public Guid Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Status { get; init; } = GenericImportJobStatus.Draft;
    public GenericImportParametersDto Parameters { get; init; } = null!;
    public string FileName { get; init; } = string.Empty;
    public int ProgressCurrent { get; init; }
    public int ProgressTotal { get; init; }
    public string? ProgressMessage { get; init; }
    public string? ResultMessage { get; init; }
    public IReadOnlyList<GenericImportDocumentResultDto> Results { get; init; } = [];
    public int DocumentsCreated { get; init; }
    public int DocumentsFailed { get; init; }
    public int DocumentsSkipped { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? QueuedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }

    public bool IsFinished => Status is GenericImportJobStatus.Completed or GenericImportJobStatus.Failed;
}
