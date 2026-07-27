using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de condiciones de pago (OCTG) de la compañía SAP activa.</summary>
public sealed class PaymentTermsCatalogService : IPaymentTermsCatalogService
{
    private readonly IHanaService _hana;

    public PaymentTermsCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<PaymentTermsDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["TO_VARCHAR(\"GroupNum\")", "\"PymntGroup\""], limit);

        var sql = $"""
            SELECT "GroupNum" AS "PaymentTermsCode", "PymntGroup" AS "PaymentTermsName"
            FROM "OCTG" WHERE {whereSql}
            ORDER BY "PymntGroup"
            {limitSql}
            """;

        return await _hana.QueryAsync<PaymentTermsDto>(sql, parameters, ct);
    }
}
