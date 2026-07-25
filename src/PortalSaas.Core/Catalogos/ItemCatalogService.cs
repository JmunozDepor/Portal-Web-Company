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

        var sql = $"""
            SELECT "ItemCode", "ItemName" FROM "OITM"
            WHERE "ItemCode" LIKE :texto OR "ItemName" LIKE :texto
            ORDER BY "ItemName" LIMIT {clampedLimit}
            """;

        return await _hana.QueryAsync<ItemDto>(sql, new { texto = $"%{searchText}%" }, ct);
    }
}
