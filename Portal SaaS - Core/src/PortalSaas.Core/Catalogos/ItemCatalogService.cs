using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>
/// Catálogo de artículos (OITM) de la compañía SAP activa -- solo búsqueda, nunca
/// listado completo (la tabla real puede tener cientos de miles de filas).
/// </summary>
public sealed class ItemCatalogService : IItemCatalogService
{
    private readonly IHanaService _hana;

    public ItemCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<ItemDto>> SearchAsync(string text, int limit = 30, CancellationToken ct = default)
    {
        var searchText = text.Trim();
        if (searchText.Length == 0)
        {
            return [];
        }

        // CatalogSqlHelper (no el SQL de :texto repetido de antes) -- bug real
        // confirmado (2026-08-02): reusar el mismo nombre de parámetro en más de una
        // posición del OR no bindea todas las apariciones en HANA
        // (Sap.Data.Hana.HanaException: "Parameter/Column (2) not bound."), único
        // catálogo del namespace que había quedado con el SQL viejo sin migrar a este
        // helper -- el resto (Cliente/Proveedor/etc.) ya lo usa justo para evitar esto.
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["\"ItemCode\"", "\"ItemName\""], limit);

        var sql = $"""
            SELECT "ItemCode", "ItemName" FROM "OITM"
            WHERE {whereSql}
            ORDER BY "ItemName" {limitSql}
            """;

        return await _hana.QueryAsync<ItemDto>(sql, parameters, ct);
    }

    public Task<IReadOnlyList<ItemDto>> GetByCodesAsync(IReadOnlyCollection<string> itemCodes, CancellationToken ct = default) =>
        CatalogSqlHelper.InChunksAsync(itemCodes, chunk => GetByCodesChunkAsync(chunk, ct));

    private async Task<IReadOnlyList<ItemDto>> GetByCodesChunkAsync(IReadOnlyCollection<string> itemCodes, CancellationToken ct = default)
    {
        if (itemCodes.Count == 0)
        {
            return [];
        }

        var parameters = new Dictionary<string, object?>();
        var placeholders = new List<string>();
        var i = 0;
        foreach (var itemCode in itemCodes)
        {
            var name = $"itemCode{i++}";
            placeholders.Add($":{name}");
            parameters[name] = itemCode;
        }

        var sql = $"""
            SELECT "ItemCode", "ItemName" FROM "OITM"
            WHERE "ItemCode" IN ({string.Join(",", placeholders)})
            """;

        return await _hana.QueryAsync<ItemDto>(sql, parameters, ct);
    }

    public Task<IReadOnlyDictionary<string, string>> GetItemCodesByBarcodesAsync(IReadOnlyCollection<string> barcodes, CancellationToken ct = default) =>
        CatalogSqlHelper.InChunksAsync(barcodes, chunk => GetItemCodesByBarcodesChunkAsync(chunk, ct), StringComparer.OrdinalIgnoreCase);

    private async Task<IReadOnlyDictionary<string, string>> GetItemCodesByBarcodesChunkAsync(IReadOnlyCollection<string> barcodes, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (barcodes.Count == 0)
        {
            return result;
        }

        var parameters = new Dictionary<string, object?>();
        var placeholders = new List<string>();
        var i = 0;
        foreach (var barcode in barcodes)
        {
            var name = $"barcode{i++}";
            placeholders.Add($":{name}");
            parameters[name] = barcode;
        }

        var inList = string.Join(",", placeholders);
        // Barra principal del maestro (OITM.CodeBars) + barras adicionales por unidad de
        // medida (OBCD) -- UNION descarta pares repetidos (misma barra en las dos tablas
        // para el mismo artículo).
        var sql = $"""
            SELECT "CodeBars" AS "Barcode", "ItemCode" FROM "OITM" WHERE "CodeBars" IN ({inList})
            UNION
            SELECT "BcdCode" AS "Barcode", "ItemCode" FROM "OBCD" WHERE "BcdCode" IN ({inList})
            """;

        var rows = await _hana.QueryDynamicAsync(sql, parameters, ct);
        foreach (var group in rows
                     .Select(r => (Barcode: r["Barcode"]?.ToString()?.Trim(), ItemCode: r["ItemCode"]?.ToString()?.Trim()))
                     .Where(r => !string.IsNullOrEmpty(r.Barcode) && !string.IsNullOrEmpty(r.ItemCode))
                     .GroupBy(r => r.Barcode!, StringComparer.OrdinalIgnoreCase))
        {
            var itemCodes = group.Select(r => r.ItemCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // Barra que apunta a más de un artículo: ambigua, se deja fuera (la fila queda
            // con error) en vez de elegir uno al azar.
            if (itemCodes.Count == 1)
            {
                result[group.Key] = itemCodes[0];
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<ItemDto>> GetTopAsync(int limit = 30, CancellationToken ct = default)
    {
        // Clamp explícito, mismo criterio que CatalogSqlHelper.BuildSearchFilter -- un
        // LIMIT nunca sin tope aunque el caller lo pida sin límite.
        var clampedLimit = Math.Clamp(limit, 1, 100);
        var sql = $"""
            SELECT "ItemCode", "ItemName" FROM "OITM"
            ORDER BY "ItemName"
            LIMIT {clampedLimit}
            """;

        return await _hana.QueryAsync<ItemDto>(sql, ct: ct);
    }

    public Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> itemCodes, CancellationToken ct = default) =>
        CatalogSqlHelper.InChunksAsync(itemCodes, chunk => GetActiveStatusChunkAsync(chunk.ToList(), ct), StringComparer.OrdinalIgnoreCase);

    private async Task<IReadOnlyDictionary<string, bool>> GetActiveStatusChunkAsync(IReadOnlyList<string> itemCodes, CancellationToken ct = default)
    {
        if (itemCodes.Count == 0)
        {
            return new Dictionary<string, bool>();
        }

        var parameters = new Dictionary<string, object?>();
        var placeholders = new List<string>();
        var i = 0;
        foreach (var itemCode in itemCodes)
        {
            var name = $"itemCode{i++}";
            placeholders.Add($":{name}");
            parameters[name] = itemCode;
        }

        var sql = $"""
            SELECT "ItemCode" AS "ItemCode", "validFor" AS "ValidFor" FROM "OITM"
            WHERE "ItemCode" IN ({string.Join(",", placeholders)})
            """;

        var rows = await _hana.QueryAsync<ItemActiveStatusRow>(sql, parameters, ct);
        return rows.ToDictionary(r => r.ItemCode, r => string.Equals(r.ValidFor, "Y", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ItemActiveStatusRow
    {
        public string ItemCode { get; init; } = null!;
        public string ValidFor { get; init; } = null!;
    }
}
