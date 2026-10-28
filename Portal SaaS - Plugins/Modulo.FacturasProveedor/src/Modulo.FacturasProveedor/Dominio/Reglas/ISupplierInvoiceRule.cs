namespace Modulo.FacturasProveedor.Dominio.Reglas;

/// <summary>Acciones de una regla y de la decisión final (valores guardados en la base).</summary>
public static class RuleActions
{
    public const string Reject = "reject";
    /// <summary>Avisar primero (se puede corregir asociando la OC) y rechazar si no se corrige en N días.</summary>
    public const string WarnThenReject = "warn_then_reject";
    public const string ManualReview = "manual_review";
    public const string Accept = "accept";

    /// <summary>Severidad para la precedencia: rechazar &gt; avisar y rechazar &gt; revisión manual.</summary>
    public static int Severity(string action) => action switch
    {
        Reject => 3,
        WarnThenReject => 2,
        ManualReview => 1,
        _ => 0,
    };
}

/// <summary>Condición de un proveedor con excepción.</summary>
public static class SupplierConditions
{
    /// <summary>No trabaja con OC: las reglas de OC no aplican y siempre requiere aprobación manual.</summary>
    public const string FixedMonthly = "fixed_monthly";
    /// <summary>Se evalúan las reglas, pero ninguna responde sola: decide una persona.</summary>
    public const string AlwaysManual = "always_manual";
    /// <summary>Ajuste regla por regla.</summary>
    public const string Custom = "custom";

    public static readonly IReadOnlyList<string> All = new[] { FixedMonthly, AlwaysManual, Custom };
}

/// <summary>Modo de un ajuste por proveedor sobre una regla.</summary>
public static class RuleOverrideModes
{
    public const string NotApplicable = "not_applicable";
    public const string Custom = "custom";
}

/// <summary>A quién aplica una regla.</summary>
public enum RuleScope
{
    /// <summary>Proveedores que trabajan con OC (todos salvo los de gasto fijo mensual).</summary>
    OcSuppliers,
    /// <summary>Proveedores marcados en Proveedores con excepciones.</summary>
    MarkedSuppliers,
    AllSuppliers,
    /// <summary>Regla temporal sobre facturas pendientes (la ejecuta el orquestador por vencimiento, no la evaluación).</summary>
    Pending,
}

public enum RuleOutcomeKind { Pass, NotApplicable, Objection }

/// <summary>Parámetro numérico de una regla (días, porcentaje, monto), con su rango válido.</summary>
public sealed record RuleParameterDefinition(string Key, string Label, decimal DefaultValue, decimal Min, decimal Max, string Unit);

/// <summary>Configuración efectiva de una regla (empresa + excepción del proveedor).</summary>
public sealed record EffectiveRuleSettings(bool IsActive, string Action, int? ActionDays, IReadOnlyDictionary<string, decimal> Parameters)
{
    public decimal Param(string key, decimal fallback) => Parameters.TryGetValue(key, out var v) ? v : fallback;
}

/// <summary>
/// Resultado de evaluar una regla. En una objeción, Action null = usar la acción configurada; con valor, la regla la
/// fuerza (ej. una OC ambigua siempre va a revisión manual).
/// </summary>
public sealed record RuleOutcome(RuleOutcomeKind Kind, string? Action, string? Message, string? DataJson)
{
    public static RuleOutcome Pass { get; } = new(RuleOutcomeKind.Pass, null, null, null);
    public static RuleOutcome NotApplicable { get; } = new(RuleOutcomeKind.NotApplicable, null, null, null);

    public static RuleOutcome Objection(string message, string? dataJson = null, string? forcedAction = null) =>
        new(RuleOutcomeKind.Objection, forcedAction, message, dataJson);
}

/// <summary>Datos de la factura recibida que usan las reglas. TotalAmount = MntTotal (con IVA o exento).</summary>
public sealed record InvoiceFacts(
    long ReceivedId, string IssuerTaxId, int DteType, long Folio, DateOnly IssueDate, DateOnly SiiReceivedOn, long TotalAmount,
    IReadOnlyList<int> PurchaseOrderDocNums);

/// <summary>Una OC de SAP referenciada por la factura, con lo ya facturado contra ella (total, con IVA o exento).</summary>
public sealed record PurchaseOrderFacts(
    int DocNum, int DocEntry, string Series, string CardCode, string? SupplierTaxId, DateOnly DocDate, decimal TotalLocal,
    string Status, decimal AlreadyInvoicedTotal);

public sealed record RuleOverrideFacts(string Mode, string? Action, int? ActionDays, IReadOnlyDictionary<string, decimal>? Parameters);

/// <summary>Excepción vigente del proveedor (Proveedores con excepciones).</summary>
public sealed record SupplierProfileFacts(string Condition, IReadOnlyDictionary<string, RuleOverrideFacts> Overrides, long? ApproverGroupId);

public sealed record SupplierFacts(bool ExistsInSap, bool IsActiveInSap, string? CardCode, SupplierProfileFacts? Profile);

/// <summary>
/// Todo lo que necesita una regla, ya traído de SAP y de la bandeja (las reglas no hacen I/O). UnresolvedDocNums =
/// OC referenciadas que no existen en SAP; AmbiguousDocNums = DocNum que existe en más de una serie.
/// </summary>
public sealed record InvoiceEvaluationContext(
    InvoiceFacts Invoice, SupplierFacts Supplier, IReadOnlyList<PurchaseOrderFacts> PurchaseOrders,
    IReadOnlyList<int> UnresolvedDocNums, IReadOnlyList<int> AmbiguousDocNums);

/// <summary>
/// Una regla del catálogo de revisión. Cada regla es una clase registrada en DI (mismo patrón que las reglas de
/// Importación Genérica); una regla nueva aparece sola en la pantalla de configuración con sus valores por defecto.
/// </summary>
public interface ISupplierInvoiceRule
{
    string Code { get; }
    string Name { get; }
    string Description { get; }
    RuleScope Scope { get; }
    IReadOnlyList<RuleParameterDefinition> Parameters { get; }
    IReadOnlyList<string> AllowedActions { get; }
    string DefaultAction { get; }
    int? DefaultActionDays { get; }
    bool DefaultActive { get; }
    bool CanBeDisabled { get; }
    int DefaultOrder { get; }

    RuleOutcome Evaluate(InvoiceEvaluationContext ctx, EffectiveRuleSettings settings);
}

/// <summary>Configuración de la empresa: por regla, más el orden y la acción cuando ninguna regla objeta.</summary>
public sealed record CompanyRuleConfiguration(
    IReadOnlyDictionary<string, EffectiveRuleSettings> ByRule, IReadOnlyDictionary<string, int> OrderByRule,
    string FinalActionWhenNoObjection);

public sealed record RuleResult(string RuleCode, RuleOutcomeKind Kind, string? Action, string? Message, string? DataJson);

/// <summary>
/// Decisión del motor. Action: accept / reject / warn_then_reject / manual_review. RecommendedAction: en proveedores
/// "siempre revisión manual", lo que habrían decidido las reglas (la persona decide).
/// </summary>
public sealed record InvoiceDecision(
    string Action, string? RecommendedAction, int? WarnDays, string? WinningRuleCode, string? Message, IReadOnlyList<RuleResult> Results);
