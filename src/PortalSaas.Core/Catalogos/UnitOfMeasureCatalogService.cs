using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de unidades de medida (OUOM) de la compañía SAP activa.</summary>
public sealed class UnitOfMeasureCatalogService : IUnitOfMeasureCatalogService
{
    private readonly IHanaService _hana;

    public UnitOfMeasureCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<UnitOfMeasureDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["TO_VARCHAR(\"UomEntry\")", "\"UomName\""], limit);

        var sql = $"""
            SELECT "UomEntry" AS "UomCode", "UomName" AS "UomName"
            FROM "OUOM" WHERE {whereSql}
            ORDER BY "UomName"
            {limitSql}
            """;

        return await _hana.QueryAsync<UnitOfMeasureDto>(sql, parameters, ct);
    }
}
