namespace PortalSaas.Data.Entities;

/// <summary>
/// Una regla del catálogo de validación pre-carga activada para un GenericImportConfig
/// puntual (ver docs/superpowers/specs/2026-09-07-reglas-validacion-importacion-
/// generica-design.md). RuleType/Severity como string -- PortalSaas.Data nunca
/// referencia PortalSaas.Abstractions (mismo criterio que GenericImportConfig.Module).
/// </summary>
public sealed class GenericImportValidationRuleAssignment
{
    public int Id { get; set; }

    public int ConfigId { get; set; }
    public GenericImportConfig Config { get; set; } = null!;

    /// <summary>Nombre del enum GenericImportValidationRuleType (ej. "PriceVsFixedList").</summary>
    public string RuleType { get; set; } = null!;

    /// <summary>"Block" | "Warning".</summary>
    public string Severity { get; set; } = "Warning";

    public bool IsActive { get; set; } = true;

    /// <summary>JSON con los parámetros del tipo de regla (ej. {"priceListNum":2,"tolerancePercent":1.5}). Null si el tipo no tiene parámetros propios.</summary>
    public string? ParametersJson { get; set; }
}
