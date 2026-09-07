using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>La Sucursal (Branch) de la fila corresponde a una dirección de despacho (CRD1, AddressType='S') del CardCode de esa fila.</summary>
public sealed class CustomerBranchValidRule : IGenericImportValidationRule
{
    private readonly ICustomerShipToAddressService _shipToAddresses;

    public CustomerBranchValidRule(ICustomerShipToAddressService shipToAddresses)
    {
        _shipToAddresses = shipToAddresses;
    }

    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.CustomerBranchValid;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = [GenericImportModule.Sales, GenericImportModule.Purchase];
    public bool SeverityIsConfigurable => true;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var relevantRows = rows.Where(r => r.BusinessPartnerCardCode is not null && !string.IsNullOrWhiteSpace(r.Branch)).ToList();
        if (relevantRows.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var addressesByCardCode = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var cardCode in relevantRows.Select(r => r.BusinessPartnerCardCode!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            addressesByCardCode[cardCode] = await _shipToAddresses.GetShipToAddressCodesAsync(cardCode, ct);
        }

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in relevantRows)
        {
            var addresses = addressesByCardCode.GetValueOrDefault(row.BusinessPartnerCardCode!, []);
            if (!addresses.Contains(row.Branch!, StringComparer.OrdinalIgnoreCase))
            {
                result[row.RowNumber] = [$"La sucursal \"{row.Branch}\" no pertenece al cliente \"{row.BusinessPartnerCardCode}\"."];
            }
        }
        return result;
    }
}
