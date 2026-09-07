namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Una regla del catálogo activada (o no) para un GenericImportConfig puntual.
/// Parameters usa claves específicas de cada RuleType -- PriceVsFixedList espera
/// "priceListNum" (int) y "tolerancePercent" (decimal); PriceVsCustomerList/
/// StockAvailable/CustomerActiveInSap/ItemActiveInSap/CustomerBranchValid solo
/// "tolerancePercent" (decimal, StockAvailable la aplica al déficit, las demás no la
/// usan -- ver cada Rule.ValidateAsync). Id = 0 para una asignación nueva sin persistir.
/// </summary>
public sealed record GenericImportValidationRuleAssignmentDto(
    int Id,
    GenericImportValidationRuleType RuleType,
    GenericImportValidationSeverity Severity,
    bool IsActive,
    IReadOnlyDictionary<string, object?> Parameters);
