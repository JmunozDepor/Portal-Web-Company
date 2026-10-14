namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Catálogo de tipos de regla de validación pre-carga del módulo de importación
/// genérica -- ver docs/superpowers/specs/2026-09-07-reglas-validacion-importacion-
/// generica-design.md. PositiveQuantity/ValidDiscountPercent son estructurales (siempre
/// activas, severidad fija en Block, nunca aparecen en la UI de configuración por
/// Formato -- reemplazan a IGenericImportValidationRule.BuiltInRules). Los otros 6 se
/// activan/parametrizan por GenericImportConfig (ver GenericImportValidationRuleAssignmentDto).
/// </summary>
public enum GenericImportValidationRuleType
{
    PositiveQuantity,
    ValidDiscountPercent,
    CustomerActiveInSap,
    ItemActiveInSap,
    PriceVsFixedList,
    PriceVsCustomerList,
    StockAvailable,
    CustomerBranchValid,

    /// <summary>
    /// Descuento TOTAL de la OC (documento) vs. el precio SAP: (Σ SAP − Σ OC) / Σ SAP no
    /// puede superar "maxDiscountPercent" -- se evalúa por documento, no por línea
    /// (2026-10-14). El hallazgo se marca en todas las líneas de esa OC.
    /// </summary>
    OrderTotalDiscount,

    /// <summary>La OC del archivo ya existe en SAP (documento no cancelado del mismo tipo y mismo socio) -- evita cargas duplicadas (2026-10-14).</summary>
    DuplicateOrderInSap,
}
