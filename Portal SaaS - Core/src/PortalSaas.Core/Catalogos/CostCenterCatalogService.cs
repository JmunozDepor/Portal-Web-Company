using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>
/// Catálogo de Centro de Costos (OPRC, DimCode=1) de la compañía SAP activa -- ver
/// ICostCenterCatalogService para la advertencia sobre el mapeo de DimCode. "Locked"
/// (no "Active") es la columna real de vigencia en OPRC, confirmado contra la
/// referencia.
/// </summary>
public sealed class CostCenterCatalogService : ICostCenterCatalogService
{
    private const int CostCenterDimCode = 1;

    private readonly IHanaService _hana;

    public CostCenterCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public Task<IReadOnlyList<CostCenterDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default) =>
        ListByDimensionAsync(CostCenterDimCode, searchText, limit, ct);

    public async Task<IReadOnlyList<CostCenterDto>> ListByDimensionAsync(int dimCode, string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["\"PrcCode\"", "\"PrcName\""], limit,
            fixedWhere: "\"DimCode\" = :dimCode AND \"Locked\" = 'N'",
            extraParameters: new Dictionary<string, object?> { ["dimCode"] = dimCode });

        var sql = $"""
            SELECT "PrcCode" AS "Code", "PrcName" AS "Name"
            FROM "OPRC" WHERE {whereSql}
            ORDER BY "PrcName"
            {limitSql}
            """;

        return await _hana.QueryAsync<CostCenterDto>(sql, parameters, ct);
    }
}
