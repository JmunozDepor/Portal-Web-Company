using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>
/// El socio de negocio de la fila está Activo en SAP -- Cliente (OCRD, CardType='C')
/// para Venta, Proveedor (CardType='S') para Compra. Sin BusinessPartnerCardCode en la
/// fila (modo normal, socio fijo del wizard -- ver GenericImportRowDto.
/// BusinessPartnerCardCode) no aplica, esta regla solo valida carga multi-socio.
/// </summary>
public sealed class CustomerActiveInSapRule : IGenericImportValidationRule
{
    private readonly ICustomerCatalogService _customers;
    private readonly ISupplierCatalogService _suppliers;

    public CustomerActiveInSapRule(ICustomerCatalogService customers, ISupplierCatalogService suppliers)
    {
        _customers = customers;
        _suppliers = suppliers;
    }

    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.CustomerActiveInSap;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = [GenericImportModule.Sales, GenericImportModule.Purchase];
    public bool SeverityIsConfigurable => true;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var cardCodes = rows.Where(r => r.BusinessPartnerCardCode is not null)
            .Select(r => r.BusinessPartnerCardCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cardCodes.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var activeStatus = module == GenericImportModule.Purchase
            ? await _suppliers.GetActiveStatusAsync(cardCodes, ct)
            : await _customers.GetActiveStatusAsync(cardCodes, ct);

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in rows)
        {
            if (row.BusinessPartnerCardCode is { } cardCode && activeStatus.TryGetValue(cardCode, out var isActive) && !isActive)
            {
                result[row.RowNumber] = [$"El socio de negocio \"{cardCode}\" está inactivo en SAP."];
            }
        }
        return result;
    }
}
