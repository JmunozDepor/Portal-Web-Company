using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Stock por almacén (OITW + OWHS) de un artículo puntual, para el visor Maestro de Producto.</summary>
public sealed class ItemStockService : IItemStockService
{
    private readonly IHanaService _hana;

    public ItemStockService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<WarehouseStockDto>> GetStockByItemAsync(string itemCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT T0."WhsCode" AS "WhsCode", T1."WhsName" AS "WhsName",
                   T0."OnHand" AS "OnHand", T0."IsCommited" AS "IsCommited"
            FROM "OITW" T0
            JOIN "OWHS" T1 ON T1."WhsCode" = T0."WhsCode"
            WHERE T0."ItemCode" = :itemCode
            ORDER BY T1."WhsName"
            """;

        return await _hana.QueryAsync<WarehouseStockDto>(sql, new { itemCode }, ct);
    }

    public async Task<IReadOnlyDictionary<(string ItemCode, string WhsCode), decimal>> GetAvailableStockAsync(
        IReadOnlyList<(string ItemCode, string WhsCode)> pairs, CancellationToken ct = default)
    {
        if (pairs.Count == 0)
        {
            return new Dictionary<(string, string), decimal>();
        }

        // Por almacén: "WhsCode = :whs AND ItemCode IN (...)" en tramos de
        // CatalogSqlHelper.MaxInListSize -- antes era un "(ItemCode = :x AND WhsCode = :y)
        // OR ..." con DOS parámetros por par: con miles de líneas, miles de OR (lento en
        // HANA, rechazado por SQL Server pasando 2.100 parámetros).
        var result = new Dictionary<(string ItemCode, string WhsCode), decimal>();
        foreach (var byWarehouse in pairs.GroupBy(p => p.WhsCode))
        {
            var itemCodes = byWarehouse.Select(p => p.ItemCode).Distinct().ToList();
            foreach (var chunk in itemCodes.Chunk(CatalogSqlHelper.MaxInListSize))
            {
                foreach (var (key, available) in await GetAvailableStockChunkAsync(byWarehouse.Key, chunk, ct))
                {
                    result[key] = available;
                }
            }
        }
        return result;
    }

    private async Task<IReadOnlyDictionary<(string ItemCode, string WhsCode), decimal>> GetAvailableStockChunkAsync(
        string whsCode, IReadOnlyList<string> itemCodes, CancellationToken ct)
    {
        var parameters = new Dictionary<string, object?> { ["whsCode"] = whsCode };
        var placeholders = new List<string>();
        for (var i = 0; i < itemCodes.Count; i++)
        {
            placeholders.Add($":itemCode{i}");
            parameters[$"itemCode{i}"] = itemCodes[i];
        }

        var sql = $"""
            SELECT T0."ItemCode" AS "ItemCode", T0."WhsCode" AS "WhsCode",
                   T0."OnHand" AS "OnHand", T0."IsCommited" AS "IsCommited"
            FROM "OITW" T0
            WHERE T0."WhsCode" = :whsCode AND T0."ItemCode" IN ({string.Join(",", placeholders)})
            """;

        var rows = await _hana.QueryAsync<ItemWarehouseStockRow>(sql, parameters, ct);
        return rows.ToDictionary(r => (r.ItemCode, r.WhsCode), r => r.OnHand - r.IsCommited);
    }

    private sealed record ItemWarehouseStockRow
    {
        public string ItemCode { get; init; } = null!;
        public string WhsCode { get; init; } = null!;
        public decimal OnHand { get; init; }
        public decimal IsCommited { get; init; }
    }
}
