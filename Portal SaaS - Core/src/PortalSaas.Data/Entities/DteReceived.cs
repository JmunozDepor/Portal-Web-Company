namespace PortalSaas.Data.Entities;

/// <summary>
/// Documento tributario electrónico recibido de un proveedor (bandeja de recibidos de plataforma), por compañía.
/// Lo llena el sincronizador desde el operador DTE; los módulos lo leen y responden vía IDteInboxService. La
/// plataforma guarda el hecho (montos, referencias, respuesta enviada); la decisión es del módulo. Ver
/// docs/superpowers/specs/2026-10-22-pasarela-documentos-electronicos-design.md §6 y el spec de Facturas de
/// Proveedor §6.3.
/// </summary>
public sealed class DteReceived
{
    public long Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string ProviderCode { get; set; } = null!;
    public string Environment { get; set; } = null!;

    public string IssuerTaxId { get; set; } = null!;
    public string IssuerName { get; set; } = null!;
    public int DteType { get; set; }
    public long Folio { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateTimeOffset? SiiReceivedAt { get; set; }

    public long NetAmount { get; set; }
    public long ExemptAmount { get; set; }
    public long VatAmount { get; set; }
    public string? OtherTaxesJson { get; set; }
    public long TotalAmount { get; set; }

    // Estados del operador, en su texto crudo.
    public string? SiiStatus { get; set; }
    /// <summary>Estado comercial (Ley 19.983): Por_Aprobar, Aceptado, Rechazado, ...</summary>
    public string? CommercialStatus { get; set; }
    /// <summary>Estado del Registro de Reclamos (Ley 20.956): Por_Aceptar, Acepta_Contenido_Documento, Reclamo_..., ...</summary>
    public string? ClaimStatus { get; set; }
    public bool IsAssigned { get; set; }
    public bool IsVoided { get; set; }
    public string? ProviderUrl { get; set; }
    /// <summary>Por qué el SII no admite aceptar ni reclamar el documento (ej. pagado al contado); null si se puede responder.</summary>
    public string? NotRespondableReason { get; set; }

    /// <summary>Lista JSON de DteReceivedReference.</summary>
    public string ReferencesJson { get; set; } = "[]";
    /// <summary>Lista JSON de DtePurchaseOrderRef (801 del DTE + asociadas a mano).</summary>
    public string PurchaseOrderRefsJson { get; set; } = "[]";
    public string? Xml { get; set; }

    /// <summary>none / pending / sent / failed / external (DteResponseStatuses).</summary>
    public string ResponseStatus { get; set; } = "none";
    public string? ResponseAction { get; set; }
    public string? ResponseReason { get; set; }
    /// <summary>"user:&lt;guid&gt;" o "rule:&lt;código&gt;".</summary>
    public string? ResponseActor { get; set; }
    public string? ResponseKey { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public int ResponseAttempts { get; set; }
    public string? LastError { get; set; }

    // Procesamiento de un módulo posterior (Etapa 2: ingreso a SAP).
    public string? ProcessingModule { get; set; }
    public string? ProcessingState { get; set; }
    public string? ProcessingRef { get; set; }
    public DateTimeOffset? ProcessingAt { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
