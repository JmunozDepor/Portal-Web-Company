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

        // Tamaño de página SIEMPRE clamped antes de interpolar -- mismo criterio que
        // el resto del Core (nunca un valor externo directo en el LIMIT).
        var clampedLimit = Math.Clamp(limit, 1, 100);

        // UPPER(...) LIKE UPPER(:texto) -- SAP guarda ItemCode/ItemName en mayúsculas,
        // y un LIKE sin normalizar en HANA es case-sensitive por defecto (bug real: un
        // usuario tipeando en minúscula, ej. "m91", no encontraba nada).
        var sql = $"""
            SELECT "ItemCode", "ItemName" FROM "OITM"
            WHERE UPPER("ItemCode") LIKE UPPER(:texto) OR UPPER("ItemName") LIKE UPPER(:texto)
            ORDER BY "ItemName" LIMIT {clampedLimit}
            """;

        return await _hana.QueryAsync<ItemDto>(sql, new { texto = $"%{searchText}%" }, ct);
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
