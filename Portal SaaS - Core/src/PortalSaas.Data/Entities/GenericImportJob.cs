namespace PortalSaas.Data.Entities;

/// <summary>
/// Trabajo de Importación Genérica (2026-10-15) -- el archivo se guarda UNA vez en el
/// servidor al procesar (Status = "Draft") y la creación en SAP corre en segundo plano
/// (GenericImportJobWorker) con su propio ciclo de vida, no dentro de la petición HTTP:
/// cerrar la pestaña o perder la conexión ya no corta la carga a mitad. Estado, progreso
/// y reporte final quedan acá para consultarlos después ("Mis importaciones").
///
/// CompanyId (no OrganizationId) -- regla dura del proyecto para todo lo que depende del
/// SAP de una compañía. Status: "Draft" | "Queued" | "Running" | "Completed" | "Failed".
/// </summary>
public sealed class GenericImportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid UserId { get; set; }
    public string Username { get; set; } = null!;

    public string Status { get; set; } = "Draft";

    // Parámetros del asistente (los mismos de GenericImportParametersDto).
    public string Module { get; set; } = null!;
    public string DocumentType { get; set; } = null!;
    public string LineType { get; set; } = null!;
    public string? BusinessPartnerCardCode { get; set; }
    public int? ConfigId { get; set; }
    public int? SalesPersonCode { get; set; }

    public string FileName { get; set; } = null!;

    /// <summary>SHA-256 del archivo (hex) -- impide confirmar dos veces el mismo archivo mientras haya un trabajo en curso.</summary>
    public string FileHash { get; set; } = null!;

    /// <summary>Contenido del Excel. Se vacía en los trabajos terminados viejos (limpieza del worker).</summary>
    public byte[]? FileContent { get; set; }

    public int ProgressCurrent { get; set; }
    public int ProgressTotal { get; set; }
    public string? ProgressMessage { get; set; }

    /// <summary>Mensaje final (resumen o error del trabajo).</summary>
    public string? ResultMessage { get; set; }

    /// <summary>GenericImportDocumentResultDto[] serializado -- el reporte final por OC.</summary>
    public string? ResultJson { get; set; }

    public int DocumentsCreated { get; set; }
    public int DocumentsFailed { get; set; }
    public int DocumentsSkipped { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
