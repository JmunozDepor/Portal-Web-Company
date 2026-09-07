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
}
