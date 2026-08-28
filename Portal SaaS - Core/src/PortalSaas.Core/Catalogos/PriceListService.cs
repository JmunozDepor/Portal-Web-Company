using PortalSaas.Abstractions.Contratos;

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

    public async Task<IReadOnlyDictionary<string, decimal>> GetPricesAsync(IReadOnlyCollection<string> itemCodes, int priceList, CancellationToken ct = default)
    {
        if (itemCodes.Count == 0)
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
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
            SELECT "ItemCode" AS "ItemCode", "Price" AS "Price"
            FROM "ITM1"
            WHERE "PriceList" = :priceList AND "ItemCode" IN ({string.Join(",", placeholders)})
            """;

        var rows = await _hana.QueryAsync<ItemPriceDto>(sql, parameters, ct);
        return rows.ToDictionary(r => r.ItemCode, r => r.Price, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Fila de ITM1 -- non-positional, HanaService la instancia vía reflection (ver GeneralLedgerAccountDto). Interna -- solo la usa PriceListService, nunca se expone.</summary>
internal sealed record ItemPriceDto
{
    public string ItemCode { get; init; } = string.Empty;
    public decimal Price { get; init; }
}
