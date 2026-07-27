using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de monedas (OCRN) de la compañía SAP activa.</summary>
public sealed class CurrencyCatalogService : ICurrencyCatalogService
{
    private readonly IHanaService _hana;

    public CurrencyCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<CurrencyDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["\"CurrCode\"", "\"CurrName\""], limit);

        var sql = $"""
            SELECT "CurrCode" AS "CurrencyCode", "CurrName" AS "CurrencyName"
            FROM "OCRN" WHERE {whereSql}
            ORDER BY "CurrName"
            {limitSql}
            """;

        return await _hana.QueryAsync<CurrencyDto>(sql, parameters, ct);
    }
}
