using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de almacenes (OWHS) de la compañía SAP activa.</summary>
public sealed class WarehouseCatalogService : IWarehouseCatalogService
{
    private readonly IHanaService _hana;

    public WarehouseCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<WarehouseDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["\"WhsCode\"", "\"WhsName\""], limit, fixedWhere: "\"Inactive\" = 'N'");

        var sql = $"""
            SELECT "WhsCode" AS "WarehouseCode", "WhsName" AS "WarehouseName"
            FROM "OWHS" WHERE {whereSql}
            ORDER BY "WhsName"
            {limitSql}
            """;

        return await _hana.QueryAsync<WarehouseDto>(sql, parameters, ct);
    }
}
