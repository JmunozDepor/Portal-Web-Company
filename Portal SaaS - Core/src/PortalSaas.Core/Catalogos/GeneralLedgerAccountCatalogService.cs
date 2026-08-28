using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de cuentas contables (OACT) de la compañía SAP activa.</summary>
public sealed class GeneralLedgerAccountCatalogService : IGeneralLedgerAccountCatalogService
{
    private readonly IHanaService _hana;

    public GeneralLedgerAccountCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<GeneralLedgerAccountDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["\"AcctCode\"", "\"AcctName\""], limit);

        var sql = $"""
            SELECT "AcctCode" AS "AccountCode", "AcctName" AS "AccountName"
            FROM "OACT" WHERE {whereSql}
            ORDER BY "AcctName"
            {limitSql}
            """;

        return await _hana.QueryAsync<GeneralLedgerAccountDto>(sql, parameters, ct);
    }
}
