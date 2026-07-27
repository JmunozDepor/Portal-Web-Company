using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de códigos de impuesto (OVTG) de la compañía SAP activa.</summary>
public sealed class TaxCodeCatalogService : ITaxCodeCatalogService
{
    private readonly IHanaService _hana;

    public TaxCodeCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<TaxCodeDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["\"Code\"", "\"Name\""], limit, fixedWhere: "\"Locked\" = 'N'");

        var sql = $"""
            SELECT "Code" AS "Code", "Name" AS "Name", "Rate" AS "Rate"
            FROM "OVTG" WHERE {whereSql}
            ORDER BY "Name"
            {limitSql}
            """;

        return await _hana.QueryAsync<TaxCodeDto>(sql, parameters, ct);
    }
}
