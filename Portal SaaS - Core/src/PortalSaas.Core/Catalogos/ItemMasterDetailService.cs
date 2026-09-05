using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Ficha de artículo (OITM + OITB) de la compañía SAP activa, para el visor Maestro de Producto.</summary>
public sealed class ItemMasterDetailService : IItemMasterDetailService
{
    private readonly IHanaService _hana;

    public ItemMasterDetailService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<ItemMasterDetailDto?> GetDetailAsync(string itemCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT T0."ItemCode" AS "ItemCode", T0."ItemName" AS "ItemName", T0."CodeBars" AS "CodeBars",
                   T0."ItmsGrpCod" AS "ItemGroupCode", T1."ItmsGrpNam" AS "ItemGroupName"
            FROM "OITM" T0
            LEFT JOIN "OITB" T1 ON T1."ItmsGrpCod" = T0."ItmsGrpCod"
            WHERE T0."ItemCode" = :itemCode
            """;

        var filas = await _hana.QueryAsync<ItemMasterDetailDto>(sql, new { itemCode }, ct);
        return filas.FirstOrDefault();
    }
}
