namespace PortalSaas.Abstractions.Modelos;

/// <summary>Estados de un trabajo de importación -- ver GenericImportJob (Data).</summary>
public static class GenericImportJobStatus
{
    public const string Draft = "Draft";

    /// <summary>
    /// Guardado sin enviar (borradores etapa 2, 2026-10-21): el archivo ya validado queda
    /// esperando (corregir precio/stock en SAP, confirmación del cliente). Sin edición de
    /// documentos: se revalida contra SAP y se envía tal cual, o se descarta.
    /// </summary>
    public const string Saved = "Saved";

    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

/// <summary>Un trabajo de importación (sin el archivo) -- "Mis importaciones" y su detalle.</summary>
public sealed record GenericImportJobDto
{
    public Guid Id { get; init; }

    /// <summary>Usuario que subió el archivo (creador).</summary>
    public Guid UserId { get; init; }
    public string Username { get; init; } = string.Empty;

    /// <summary>Motivo por el que se guardó sin enviar (obligatorio al guardar).</summary>
    public string? SavedReason { get; init; }
    public DateTimeOffset? SavedAt { get; init; }

    /// <summary>Quién lo envió a SAP, si no fue el creador (una carga guardada la envía cualquiera con permiso).</summary>
    public string? SubmittedByUsername { get; init; }

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

    public bool IsSaved => Status == GenericImportJobStatus.Saved;
}
