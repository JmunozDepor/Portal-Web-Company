using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Ver IBusinessPartnerDefaultsService -- lectura directa a HANA (OCRD/OSLP), sin Service Layer, portado de SocioNegocioDefaultsService en referencia-original/PortalSAP_v2.</summary>
public sealed class BusinessPartnerDefaultsService : IBusinessPartnerDefaultsService
{
    private readonly IHanaService _hana;

    public BusinessPartnerDefaultsService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<BusinessPartnerDefaultsDto?> GetAsync(string cardCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT c."CardCode" AS "CardCode", c."SlpCode" AS "SalesEmployeeCode", s."SlpName" AS "SalesEmployeeName",
                   c."ListNum" AS "PriceListCode"
            FROM "OCRD" c
            LEFT JOIN "OSLP" s ON s."SlpCode" = c."SlpCode"
            WHERE c."CardCode" = :cardCode
            """;

        var rows = await _hana.QueryAsync<BusinessPartnerDefaultsDto>(sql, new { cardCode }, ct);
        return rows.FirstOrDefault();
    }
}
