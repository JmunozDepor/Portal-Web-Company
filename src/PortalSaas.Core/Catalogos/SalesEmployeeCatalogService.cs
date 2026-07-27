using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de vendedores (OSLP) de la compañía SAP activa.</summary>
public sealed class SalesEmployeeCatalogService : ISalesEmployeeCatalogService
{
    private readonly IHanaService _hana;

    public SalesEmployeeCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<SalesEmployeeDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["TO_VARCHAR(\"SlpCode\")", "\"SlpName\""], limit, fixedWhere: "\"Active\" = 'Y'");

        var sql = $"""
            SELECT "SlpCode" AS "SalesEmployeeCode", "SlpName" AS "SalesEmployeeName"
            FROM "OSLP" WHERE {whereSql}
            ORDER BY "SlpName"
            {limitSql}
            """;

        return await _hana.QueryAsync<SalesEmployeeDto>(sql, parameters, ct);
    }
}
