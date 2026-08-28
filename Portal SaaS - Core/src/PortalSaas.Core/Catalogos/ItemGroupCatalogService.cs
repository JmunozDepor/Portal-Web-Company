using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de grupos de artículo (OITB) de la compañía SAP activa.</summary>
public sealed class ItemGroupCatalogService : IItemGroupCatalogService
{
    private readonly IHanaService _hana;

    public ItemGroupCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<ItemGroupDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["TO_VARCHAR(\"ItmsGrpCod\")", "\"ItmsGrpNam\""], limit);

        var sql = $"""
            SELECT "ItmsGrpCod" AS "ItemGroupCode", "ItmsGrpNam" AS "ItemGroupName"
            FROM "OITB" WHERE {whereSql}
            ORDER BY "ItmsGrpNam"
            {limitSql}
            """;

        return await _hana.QueryAsync<ItemGroupDto>(sql, parameters, ct);
    }
}
