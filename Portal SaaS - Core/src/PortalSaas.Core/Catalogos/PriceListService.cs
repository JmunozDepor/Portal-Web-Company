using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Ver IPriceListService -- sobre ITM1 (Precios por lista), portado de ListaPrecioService en referencia-original/PortalSAP_v2.</summary>
public sealed class PriceListService : IPriceListService
{
    private readonly IHanaService _hana;

    public PriceListService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<decimal?> GetPriceAsync(string itemCode, int priceList, CancellationToken ct = default)
    {
        const string sql = """
            SELECT "Price" AS "Price"
            FROM "ITM1"
            WHERE "ItemCode" = :itemCode AND "PriceList" = :priceList
            """;

        var rows = await _hana.QueryAsync<ItemPriceDto>(sql, new { itemCode, priceList }, ct);
        return rows.FirstOrDefault()?.Price;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetPricesAsync(IReadOnlyCollection<string> itemCodes, int priceList, CancellationToken ct = default) =>
        (await GetPriceQuotesAsync(itemCodes, priceList, ct)).ToDictionary(p => p.Key, p => p.Value.Price, StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyDictionary<string, ItemPriceQuote>> GetPriceQuotesAsync(IReadOnlyCollection<string> itemCodes, int priceList, CancellationToken ct = default) =>
        CatalogSqlHelper.InChunksAsync(itemCodes, chunk => GetPriceQuotesChunkAsync(chunk, priceList, ct), StringComparer.OrdinalIgnoreCase);

    private async Task<IReadOnlyDictionary<string, ItemPriceQuote>> GetPriceQuotesChunkAsync(IReadOnlyCollection<string> itemCodes, int priceList, CancellationToken ct = default)
    {
        if (itemCodes.Count == 0)
        {
            return new Dictionary<string, ItemPriceQuote>(StringComparer.OrdinalIgnoreCase);
        }

        var parameters = new Dictionary<string, object?> { ["priceList"] = priceList };
        var placeholders = new List<string>();
        var i = 0;
        foreach (var itemCode in itemCodes)
        {
            var name = $"itemCode{i++}";
            placeholders.Add($":{name}");
            parameters[name] = itemCode;
        }

        var sql = $"""
            SELECT "ItemCode" AS "ItemCode", "Price" AS "Price", "Currency" AS "Currency"
            FROM "ITM1"
            WHERE "PriceList" = :priceList AND "ItemCode" IN ({string.Join(",", placeholders)})
            """;

        var rows = await _hana.QueryAsync<ItemPriceDto>(sql, parameters, ct);
        return rows.ToDictionary(r => r.ItemCode, r => new ItemPriceQuote(r.Price, NormalizeCurrency(r.Currency)), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetSpecialPricesAsync(string cardCode, IReadOnlyCollection<string> itemCodes, DateTime date,
        CancellationToken ct = default) =>
        (await GetSpecialPriceQuotesAsync(cardCode, itemCodes, date, ct)).ToDictionary(p => p.Key, p => p.Value.Price, StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyDictionary<string, ItemPriceQuote>> GetSpecialPriceQuotesAsync(string cardCode, IReadOnlyCollection<string> itemCodes, DateTime date,
        CancellationToken ct = default) =>
        CatalogSqlHelper.InChunksAsync(itemCodes, chunk => GetSpecialPriceQuotesChunkAsync(cardCode, chunk, date, ct), StringComparer.OrdinalIgnoreCase);

    private static string? NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? null : currency.Trim();

    private async Task<IReadOnlyDictionary<string, ItemPriceQuote>> GetSpecialPriceQuotesChunkAsync(string cardCode, IReadOnlyCollection<string> itemCodes, DateTime date,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, ItemPriceQuote>(StringComparer.OrdinalIgnoreCase);
        if (itemCodes.Count == 0 || string.IsNullOrWhiteSpace(cardCode))
        {
            return result;
        }

        var parameters = new Dictionary<string, object?> { ["cardCode"] = cardCode, ["date"] = date.Date };
        var placeholders = new List<string>();
        var i = 0;
        foreach (var itemCode in itemCodes)
        {
            var name = $"itemCode{i++}";
            placeholders.Add($":{name}");
            parameters[name] = itemCode;
        }
        var inList = string.Join(",", placeholders);

        // Prioridad 1 = descuento por período vigente (SPP1), 2 = precio especial general
        // (OSPP) vigente -- se queda la de menor prioridad por artículo.
        var sql = $"""
            SELECT p."ItemCode" AS "ItemCode", p."Price" AS "Price", p."Currency" AS "Currency", 1 AS "Priority"
            FROM "SPP1" p
            WHERE p."CardCode" = :cardCode AND p."ItemCode" IN ({inList})
              AND (p."FromDate" IS NULL OR p."FromDate" <= :date) AND (p."ToDate" IS NULL OR p."ToDate" >= :date)
            UNION ALL
            SELECT s."ItemCode" AS "ItemCode", s."Price" AS "Price", s."Currency" AS "Currency", 2 AS "Priority"
            FROM "OSPP" s
            WHERE s."CardCode" = :cardCode AND s."ItemCode" IN ({inList}) AND s."Valid" = 'Y'
              AND (s."ValidFrom" IS NULL OR s."ValidFrom" <= :date) AND (s."ValidTo" IS NULL OR s."ValidTo" >= :date)
            """;

        var rows = await _hana.QueryDynamicAsync(sql, parameters, ct);
        foreach (var group in rows
                     .Select(r => (ItemCode: r["ItemCode"]?.ToString()?.Trim() ?? string.Empty,
                         Price: Convert.ToDecimal(r["Price"] ?? 0m),
                         Currency: NormalizeCurrency(r["Currency"]?.ToString()),
                         Priority: Convert.ToInt32(r["Priority"] ?? 2)))
                     .Where(r => r.ItemCode.Length > 0)
                     .GroupBy(r => r.ItemCode, StringComparer.OrdinalIgnoreCase))
        {
            var best = group.OrderBy(r => r.Priority).First();
            result[group.Key] = new ItemPriceQuote(best.Price, best.Currency);
        }

        return result;
    }

    public async Task<IReadOnlyList<PriceListOptionDto>> ListAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT "ListNum" AS "ListNum", "ListName" AS "ListName"
            FROM "OPLN"
            ORDER BY "ListNum"
            """;

        return await _hana.QueryAsync<PriceListOptionDto>(sql, ct: ct);
    }
}

/// <summary>Fila de ITM1 -- non-positional, HanaService la instancia vía reflection (ver GeneralLedgerAccountDto). Interna -- solo la usa PriceListService, nunca se expone.</summary>
internal sealed record ItemPriceDto
{
    public string ItemCode { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string? Currency { get; init; }
}
