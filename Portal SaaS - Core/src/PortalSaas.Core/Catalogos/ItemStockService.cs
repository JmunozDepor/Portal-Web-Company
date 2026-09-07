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

        var parameters = new Dictionary<string, object?>();
        var clauses = new List<string>();
        var i = 0;
        foreach (var (itemCode, whsCode) in pairs)
        {
            var itemParam = $"itemCode{i}";
            var whsParam = $"whsCode{i}";
            clauses.Add($"(T0.\"ItemCode\" = :{itemParam} AND T0.\"WhsCode\" = :{whsParam})");
            parameters[itemParam] = itemCode;
            parameters[whsParam] = whsCode;
            i++;
        }

        var sql = $"""
            SELECT T0."ItemCode" AS "ItemCode", T0."WhsCode" AS "WhsCode",
                   T0."OnHand" AS "OnHand", T0."IsCommited" AS "IsCommited"
            FROM "OITW" T0
            WHERE {string.Join(" OR ", clauses)}
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
