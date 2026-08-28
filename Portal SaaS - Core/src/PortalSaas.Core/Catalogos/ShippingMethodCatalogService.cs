using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de métodos de envío (OSHP) de la compañía SAP activa.</summary>
public sealed class ShippingMethodCatalogService : IShippingMethodCatalogService
{
    private readonly IHanaService _hana;

    public ShippingMethodCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<ShippingMethodDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["TO_VARCHAR(\"TrnspCode\")", "\"TrnspName\""], limit);

        var sql = $"""
            SELECT "TrnspCode" AS "ShippingMethodCode", "TrnspName" AS "ShippingMethodName"
            FROM "OSHP" WHERE {whereSql}
            ORDER BY "TrnspName"
            {limitSql}
            """;

        return await _hana.QueryAsync<ShippingMethodDto>(sql, parameters, ct);
    }
}
