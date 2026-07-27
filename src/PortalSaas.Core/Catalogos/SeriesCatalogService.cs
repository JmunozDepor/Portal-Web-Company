using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>
/// Catálogo de series de numeración (NNM1) de la compañía SAP activa -- ver
/// ISeriesCatalogService para el detalle de por qué se filtra por ObjectCode.
/// </summary>
public sealed class SeriesCatalogService : ISeriesCatalogService
{
    private readonly IHanaService _hana;

    public SeriesCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<SeriesDto>> ListAsync(string objectCode, string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["\"SeriesName\""], limit,
            fixedWhere: "\"ObjectCode\" = :objectCode AND \"Locked\" = 'N'",
            extraParameters: new Dictionary<string, object?> { ["objectCode"] = objectCode });

        var sql = $"""
            SELECT "Series" AS "SeriesCode", "SeriesName" AS "SeriesName"
            FROM "NNM1" WHERE {whereSql}
            ORDER BY "SeriesName"
            {limitSql}
            """;

        return await _hana.QueryAsync<SeriesDto>(sql, parameters, ct);
    }
}
