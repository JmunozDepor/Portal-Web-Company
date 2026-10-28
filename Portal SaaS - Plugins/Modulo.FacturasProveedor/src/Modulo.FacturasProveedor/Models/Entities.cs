namespace Modulo.FacturasProveedor.Models;

/// <summary>Estados de una revisión de factura (fp_invoice_reviews.status).</summary>
public static class ReviewStatuses
{
    public const string New = "new";
    /// <summary>En aviso: se rechaza al vencer el aviso si nadie corrige (ej. asocia la OC).</summary>
    public const string Warning = "warning";
    public const string InReview = "in_review";
    public const string ToSend = "to_send";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    /// <summary>Decidida en modo simulación: nunca se envía al SII.</summary>
    public const string Simulated = "simulated";
    /// <summary>Respondida fuera del portal (ej. en el portal del operador).</summary>
    public const string External = "external";
    /// <summary>Pasó el plazo legal sin respuesta: la ley la da por aceptada.</summary>
    public const string Expired = "expired";
    /// <summary>Recibida antes de "procesar desde" (o sin esa fecha): solo se muestra.</summary>
    public const string OutOfScope = "out_of_scope";
    /// <summary>El SII no admite aceptarla ni reclamarla (ej. pagada al contado): solo se muestra.</summary>
    public const string NotRespondable = "not_respondable";

    public static bool IsFinal(string status) => status is Accepted or Rejected or External or Expired or OutOfScope or NotRespondable;

    /// <summary>Pendientes de decisión o de envío (bandeja "Pendientes").</summary>
    public static readonly IReadOnlyList<string> Pending = new[] { Warning, InReview, ToSend };
}

/// <summary>Modo del módulo.</summary>
public static class ModuleModes
{
    /// <summary>Evalúa y muestra la decisión, pero no responde al SII.</summary>
    public const string Simulation = "simulation";
    public const string Active = "active";
}

/// <summary>Parámetros generales del módulo por compañía (una fila).</summary>
public sealed class FpSettings
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public int LegalDeadlineDays { get; set; } = 8;
    public int EscalateDaysBefore { get; set; } = 2;
    public TimeOnly CutoffTime { get; set; } = new(10, 0);
    public string FinalActionWhenNoObjection { get; set; } = "accept";
    public DateOnly? ProcessFromDate { get; set; }
    public string Mode { get; set; } = ModuleModes.Simulation;
    public bool DailyEmailDigest { get; set; }
    public long? FinanceGroupId { get; set; }
    /// <summary>Cursor de lectura de la bandeja de plataforma (UpdatedAt, Id del último documento procesado).</summary>
    public DateTimeOffset? InboxCursor { get; set; }
    public long InboxCursorId { get; set; }
    public DateOnly? LastDigestOn { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Configuración de una regla del catálogo para la compañía.</summary>
public sealed class FpRuleSetting
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public string RuleCode { get; set; } = null!;
    public bool IsActive { get; set; }
    public string Action { get; set; } = null!;
    public int? ActionDays { get; set; }
    /// <summary>JSON { clave: valor } de los parámetros numéricos.</summary>
    public string ParametersJson { get; set; } = "{}";
    public int SortOrder { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Auditoría de cambios de configuración (la "regla explícita aprobada" que exige la pasarela).</summary>
public sealed class FpConfigEvent
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>settings / rule / supplier / group / sap_query.</summary>
    public string EntityType { get; set; } = null!;
    public string EntityKey { get; set; } = null!;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Proveedor con excepción a las reglas de la empresa.</summary>
public sealed class FpSupplierProfile
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CardCode { get; set; } = null!;
    public string TaxId { get; set; } = null!;
    public string SupplierName { get; set; } = null!;
    public string Condition { get; set; } = null!;
    public long? ApproverGroupId { get; set; }
    public long? BackupGroupId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public List<FpSupplierRuleOverride> Overrides { get; set; } = new();
}

/// <summary>Ajuste de un proveedor sobre una regla: "no aplica" u otra acción/días/parámetros.</summary>
public sealed class FpSupplierRuleOverride
{
    public long Id { get; set; }
    public long ProfileId { get; set; }
    public string RuleCode { get; set; } = null!;
    public string Mode { get; set; } = null!;
    public string? Action { get; set; }
    public int? ActionDays { get; set; }
    public string? ParametersJson { get; set; }
}

/// <summary>Grupo de usuarios del portal que decide las facturas en revisión manual.</summary>
public sealed class FpApproverGroup
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = null!;
    /// <summary>Grupo de Finanzas / pago a proveedores: supervisa, recibe escalamientos y puede decidir cualquier factura.</summary>
    public bool IsFinance { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<FpApproverGroupMember> Members { get; set; } = new();
}

public sealed class FpApproverGroupMember
{
    public long GroupId { get; set; }
    public Guid UserId { get; set; }
}

/// <summary>Revisión de una factura recibida (una por documento de la bandeja de plataforma).</summary>
public sealed class FpInvoiceReview
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public long ReceivedId { get; set; }
    public string IssuerTaxId { get; set; } = null!;
    public string IssuerName { get; set; } = null!;
    public int DteType { get; set; }
    public long Folio { get; set; }
    public long TotalAmount { get; set; }
    public DateOnly SiiReceivedOn { get; set; }
    public DateOnly LegalDeadlineOn { get; set; }
    public DateOnly? WarnDeadlineOn { get; set; }
    public string Status { get; set; } = ReviewStatuses.New;
    public string? ProposedAction { get; set; }
    public string? RecommendedAction { get; set; }
    public string? DecidedAction { get; set; }
    /// <summary>"rule:{código}", "rule:final" o "user".</summary>
    public string? DecisionSource { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? Reason { get; set; }
    /// <summary>Tipo de reclamo elegido por una persona al rechazar (claim_*); null = reclamo al contenido.</summary>
    public string? ClaimType { get; set; }
    public long? AssignedGroupId { get; set; }
    public bool IsSimulated { get; set; }
    public int DecisionNumber { get; set; }
    public int EvaluationCount { get; set; }
    public DateTimeOffset? LastEvaluatedAt { get; set; }
    public DateTimeOffset? EscalatedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Resultado de una regla en la última evaluación de una revisión.</summary>
public sealed class FpReviewRuleResult
{
    public long Id { get; set; }
    public long ReviewId { get; set; }
    public string RuleCode { get; set; } = null!;
    /// <summary>pass / not_applicable / objection.</summary>
    public string Kind { get; set; } = null!;
    public string? Action { get; set; }
    public string? Message { get; set; }
    public string? DataJson { get; set; }
}

/// <summary>Bitácora de una revisión.</summary>
public sealed class FpReviewEvent
{
    public long Id { get; set; }
    public long ReviewId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string EventType { get; set; } = null!;
    public Guid? UserId { get; set; }
    public string? Detail { get; set; }
}

/// <summary>Consulta SAP configurable por compañía (solo lectura, contrato de columnas fijo por tipo).</summary>
public sealed class FpSapQuery
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public string QueryKind { get; set; } = null!;
    public string SqlText { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ValidatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
