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

    public async Task<IReadOnlyList<ItemDto>> GetByCodesAsync(IReadOnlyCollection<string> itemCodes, CancellationToken ct = default)
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
}
