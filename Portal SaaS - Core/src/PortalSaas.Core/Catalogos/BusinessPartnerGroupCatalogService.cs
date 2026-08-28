using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de grupos de socio de negocio (OCRG) de la compañía SAP activa.</summary>
public sealed class BusinessPartnerGroupCatalogService : IBusinessPartnerGroupCatalogService
{
    private readonly IHanaService _hana;

    public BusinessPartnerGroupCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<BusinessPartnerGroupDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["TO_VARCHAR(\"GroupCode\")", "\"GroupName\""], limit);

        var sql = $"""
            SELECT "GroupCode" AS "GroupCode", "GroupName" AS "GroupName"
            FROM "OCRG" WHERE {whereSql}
            ORDER BY "GroupName"
            {limitSql}
            """;

        return await _hana.QueryAsync<BusinessPartnerGroupDto>(sql, parameters, ct);
    }
}
