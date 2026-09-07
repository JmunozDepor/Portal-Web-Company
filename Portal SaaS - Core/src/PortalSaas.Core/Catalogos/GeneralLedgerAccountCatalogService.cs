using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>
/// Catálogo de cuentas contables (OACT) de la compañía SAP activa. Solo cuentas
/// imputables (<c>"Postable" = 'Y'</c>) -- las cuentas de título/agrupación
/// (<c>Postable = 'N'</c>, ej. "Activos", "#9") no sirven para digitar una línea de
/// tipo Servicio ni para la integración contable, mismo criterio que el filtro
/// <c>"Locked" = 'N'</c> de <see cref="CostCenterCatalogService"/>.
/// </summary>
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
            searchText, ["\"AcctCode\"", "\"AcctName\""], limit,
            fixedWhere: "\"Postable\" = 'Y'");

        var sql = $"""
            SELECT "AcctCode" AS "AccountCode", "AcctName" AS "AccountName"
            FROM "OACT" WHERE {whereSql}
            ORDER BY "AcctCode"
            {limitSql}
            """;

        return await _hana.QueryAsync<GeneralLedgerAccountDto>(sql, parameters, ct);
    }
}
