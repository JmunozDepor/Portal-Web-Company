using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>
/// UnitPrice de la fila vs. el precio del artículo en una lista de precio FIJA
/// (parámetro "priceListNum"), con tolerancia "tolerancePercent" (% sobre el precio de
/// lista, en cualquier dirección). Solo Artículo con precio -- Servicio/Inventario no
/// tienen ItemCode+UnitPrice juntos en el mismo sentido, se filtran igual acá.
/// </summary>
public sealed class PriceVsFixedListRule : IGenericImportValidationRule
{
    private readonly IPriceListService _priceList;

    public PriceVsFixedListRule(IPriceListService priceList)
    {
        _priceList = priceList;
    }

    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.PriceVsFixedList;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = [GenericImportModule.Sales, GenericImportModule.Purchase];
    public bool SeverityIsConfigurable => true;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var priceListNum = Convert.ToInt32(parameters["priceListNum"]);
        var tolerancePercent = parameters.TryGetValue("tolerancePercent", out var t) && t is not null ? Convert.ToDecimal(t) : 0m;

        var itemsWithPrice = rows.Where(r => r.ItemCode is not null && r.UnitPrice is not null).ToList();
        var itemCodes = itemsWithPrice.Select(r => r.ItemCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (itemCodes.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var listPrices = await _priceList.GetPricesAsync(itemCodes, priceListNum, ct);

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in itemsWithPrice)
        {
            if (!listPrices.TryGetValue(row.ItemCode!, out var listPrice))
            {
                continue;
            }

            var tolerancia = listPrice * (tolerancePercent / 100m);
            if (Math.Abs(row.UnitPrice!.Value - listPrice) > tolerancia)
            {
                result[row.RowNumber] = [$"Precio importado ({row.UnitPrice:N2}) distinto al de la lista {priceListNum} ({listPrice:N2})."];
            }
        }
        return result;
    }
}
