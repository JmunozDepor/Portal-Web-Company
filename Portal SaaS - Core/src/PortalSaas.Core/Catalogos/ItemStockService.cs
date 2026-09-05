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
}
