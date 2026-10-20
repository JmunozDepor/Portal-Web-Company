namespace PortalSaas.Data.Entities;

/// <summary>
/// Borrador de un documento digitado en un formulario genérico (Venta/Compra/Inventario)
/// antes de enviarlo a SAP (2026-10-20, ver docs/superpowers/plans/2026-10-20-borradores-
/// documentos.md). Por compañía (regla dura: todo lo que depende del SAP de una compañía
/// cuelga de CompanyId). Todos los usuarios de la compañía lo ven; el listado filtra por
/// defecto los del creador. Payload = el InputModel del formulario serializado tal cual
/// (cabecera + líneas, incluido lo importado desde Excel), así abrirlo reconstruye el
/// formulario exacto. Sin FK al usuario creador a propósito: si el usuario se elimina, el
/// borrador sigue visible para el resto (se guarda id + nombre).
/// </summary>
public sealed class DocumentDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>"ventas" | "compras" | "inventario" -- ver DocumentDraftEngine (Abstractions).</summary>
    public string Engine { get; set; } = null!;

    /// <summary>Nombre del enum del tipo de documento del motor (ej. "SalesOrder", "InventoryTransferRequest").</summary>
    public string DocumentType { get; set; } = null!;

    /// <summary>"draft" | "sending" | "sent" | "error" -- ver DocumentDraftStatus (Abstractions).</summary>
    public string Status { get; set; } = "draft";

    /// <summary>Por qué quedó en borrador -- obligatorio al guardar.</summary>
    public string Reason { get; set; } = null!;

    public string PayloadJson { get; set; } = null!;

    // Resumen para el listado (se recalcula en cada guardado, sin abrir el payload).
    public string? PartnerCode { get; set; }
    public string? Reference { get; set; }
    public DateOnly? DocDate { get; set; }
    public int LineCount { get; set; }
    public decimal? Total { get; set; }

    public Guid CreatedByUserId { get; set; }
    public string CreatedByUsername { get; set; } = null!;
    public string UpdatedByUsername { get; set; } = null!;

    public int? SapDocEntry { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Token de concurrencia -- el paso draft -> sending es atómico (no se envía dos veces).</summary>
    public int Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>
/// Parámetros de comportamiento de documentos por compañía (1:1 con Company). Hoy solo la
/// retención de borradores -- aplica a los borradores digitados y (etapa 2) a las
/// importaciones masivas guardadas sin enviar.
/// </summary>
public sealed class CompanyDocumentSettings
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Días sin modificar tras los cuales se elimina un borrador (pendiente o enviado). Null = no vencen.</summary>
    public int? DraftRetentionDays { get; set; }

    /// <summary>
    /// UDF de cabecera (ej. "U_PortalDraftId") donde el portal escribe la marca de origen de
    /// cada documento que crea -- ver IDocumentSapMarkerService. Null = la marca va en
    /// Comentarios. Debe existir en las cabeceras de todos los tipos de documento.
    /// </summary>
    public string? DraftMarkerUdfName { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
