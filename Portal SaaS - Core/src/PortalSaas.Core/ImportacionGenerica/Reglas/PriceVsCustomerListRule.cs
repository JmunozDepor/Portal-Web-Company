using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>
/// Igual que PriceVsFixedListRule pero la lista de referencia es la asignada al
/// CardCode de cada fila (OCRD.ListNum, vía IBusinessPartnerDefaultsService) -- sin
/// lista asignada, esa fila simplemente no se valida (no es un error, el cliente no
/// tiene lista de precio configurada en SAP).
/// </summary>
public sealed class PriceVsCustomerListRule : IGenericImportValidationRule
{
    private readonly IPriceListService _priceList;
    private readonly IBusinessPartnerDefaultsService _partnerDefaults;

    public PriceVsCustomerListRule(IPriceListService priceList, IBusinessPartnerDefaultsService partnerDefaults)
    {
        _priceList = priceList;
        _partnerDefaults = partnerDefaults;
    }

    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.PriceVsCustomerList;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = [GenericImportModule.Sales, GenericImportModule.Purchase];
    public bool SeverityIsConfigurable => true;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var tolerancePercent = parameters.TryGetValue("tolerancePercent", out var t) && t is not null ? Convert.ToDecimal(t) : 0m;

        var itemsWithPrice = rows.Where(r => r.ItemCode is not null && r.UnitPrice is not null && r.BusinessPartnerCardCode is not null).ToList();
        if (itemsWithPrice.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var priceListByCardCode = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (var cardCode in itemsWithPrice.Select(r => r.BusinessPartnerCardCode!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var defaults = await _partnerDefaults.GetAsync(cardCode, ct);
            priceListByCardCode[cardCode] = defaults?.PriceListCode;
        }

        var itemCodes = itemsWithPrice.Select(r => r.ItemCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var pricesByList = new Dictionary<int, IReadOnlyDictionary<string, decimal>>();
        foreach (var priceListNum in priceListByCardCode.Values.Where(v => v is not null).Select(v => v!.Value).Distinct())
        {
            pricesByList[priceListNum] = await _priceList.GetPricesAsync(itemCodes, priceListNum, ct);
        }

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in itemsWithPrice)
        {
            var priceListNum = priceListByCardCode.GetValueOrDefault(row.BusinessPartnerCardCode!);
            if (priceListNum is null || !pricesByList.TryGetValue(priceListNum.Value, out var prices) || !prices.TryGetValue(row.ItemCode!, out var listPrice))
            {
                continue;
            }

            var tolerancia = listPrice * (tolerancePercent / 100m);
            if (Math.Abs(row.UnitPrice!.Value - listPrice) > tolerancia)
            {
                result[row.RowNumber] = [$"Precio importado ({row.UnitPrice:N2}) distinto al de la lista del cliente {priceListNum} ({listPrice:N2})."];
            }
        }
        return result;
    }
}
