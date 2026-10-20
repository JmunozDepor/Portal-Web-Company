using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Ver IDocumentLineValidationService.</summary>
public sealed class DocumentLineValidationService : IDocumentLineValidationService
{
    /// <summary>Margen de redondeo (no una tolerancia de negocio): precios iguales a 2 decimales pasan.</summary>
    private const decimal PriceEpsilon = 0.005m;

    private readonly IItemStockService _stock;
    private readonly IPriceListService _priceLists;

    public DocumentLineValidationService(IItemStockService stock, IPriceListService priceLists)
    {
        _stock = stock;
        _priceLists = priceLists;
    }

    public async Task<IReadOnlyList<DocumentLineIssue>> ValidateAsync(DocumentLineValidationRequest request, CancellationToken ct = default)
    {
        var lines = request.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.ItemCode))
            .Select(l => l with { ItemCode = l.ItemCode.Trim(), Warehouse = l.Warehouse?.Trim() })
            .ToList();

        var issues = new List<DocumentLineIssue>();
        if (lines.Count == 0)
        {
            return issues;
        }

        if (request.ValidateStock)
        {
            await ValidateStockAsync(lines, issues, ct);
        }

        if (request.ValidatePrice)
        {
            await ValidatePriceAsync(lines, request, issues, ct);
        }

        return issues;
    }

    private async Task ValidateStockAsync(List<DocumentLineCheck> lines, List<DocumentLineIssue> issues, CancellationToken ct)
    {
        var withWarehouse = lines.Where(l => !string.IsNullOrEmpty(l.Warehouse) && l.Quantity > 0).ToList();
        if (withWarehouse.Count == 0)
        {
            return;
        }

        var comparer = PairComparer.Instance;
        var demand = withWarehouse
            .GroupBy(l => (l.ItemCode, Warehouse: l.Warehouse!), comparer)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity), comparer);

        var available = await _stock.GetAvailableStockAsync(demand.Keys.Select(k => (k.ItemCode, WhsCode: k.Warehouse)).ToList(), ct);
        var availableByPair = new Dictionary<(string ItemCode, string Warehouse), decimal>(comparer);
        foreach (var (key, value) in available)
        {
            availableByPair[(key.ItemCode, key.WhsCode)] = value;
        }

        foreach (var line in withWarehouse)
        {
            var key = (line.ItemCode, Warehouse: line.Warehouse!);
            var total = demand[key];
            var disponible = availableByPair.GetValueOrDefault(key, 0m);
            if (total > disponible)
            {
                issues.Add(new DocumentLineIssue(line.Key, "Quantity",
                    $"Stock insuficiente de \"{line.ItemCode}\" en bodega \"{line.Warehouse}\": se piden {total:N0}, disponible {disponible:N0}."));
            }
        }
    }

    private async Task ValidatePriceAsync(List<DocumentLineCheck> lines, DocumentLineValidationRequest request, List<DocumentLineIssue> issues, CancellationToken ct)
    {
        var withPrice = lines.Where(l => l.UnitPrice is not null).ToList();
        if (withPrice.Count == 0)
        {
            return;
        }

        var codes = withPrice.Select(l => l.ItemCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var reference = new Dictionary<string, (ItemPriceQuote Quote, string Source)>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
        {
            var date = (request.Date ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
            foreach (var (code, quote) in await _priceLists.GetSpecialPriceQuotesAsync(request.CardCode.Trim(), codes, date, ct))
            {
                reference[code] = (quote, "precio especial del socio");
            }
        }

        var pending = codes.Where(c => !reference.ContainsKey(c)).ToList();
        if (request.PriceList is not null && pending.Count > 0)
        {
            foreach (var (code, quote) in await _priceLists.GetPriceQuotesAsync(pending, request.PriceList.Value, ct))
            {
                reference[code] = (quote, "lista de precio");
            }
        }

        foreach (var line in withPrice)
        {
            if (!reference.TryGetValue(line.ItemCode, out var expected))
            {
                continue; // sin precio de referencia: no se valida (igual que Importación Genérica)
            }

            var lineCurrency = string.IsNullOrWhiteSpace(line.Currency) ? null : line.Currency.Trim();
            if (lineCurrency is not null && expected.Quote.Currency is not null
                && !string.Equals(lineCurrency, expected.Quote.Currency, StringComparison.OrdinalIgnoreCase))
            {
                continue; // monedas distintas: SAP convierte, no se compara
            }

            if (Math.Abs(line.UnitPrice!.Value - expected.Quote.Price) > PriceEpsilon)
            {
                issues.Add(new DocumentLineIssue(line.Key, "UnitPrice",
                    $"Precio de \"{line.ItemCode}\" ({line.UnitPrice:N2}) distinto al de la {expected.Source} ({expected.Quote.Price:N2})."));
            }
        }
    }

    private sealed class PairComparer : IEqualityComparer<(string ItemCode, string Warehouse)>
    {
        public static readonly PairComparer Instance = new();

        public bool Equals((string ItemCode, string Warehouse) x, (string ItemCode, string Warehouse) y) =>
            string.Equals(x.ItemCode, y.ItemCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Warehouse, y.Warehouse, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string ItemCode, string Warehouse) obj) =>
            HashCode.Combine(obj.ItemCode.ToUpperInvariant(), obj.Warehouse.ToUpperInvariant());
    }
}
