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

    public async Task<IReadOnlyList<SalesEmployeeDto>> ListAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT "SlpCode" AS "SalesEmployeeCode", "SlpName" AS "SalesEmployeeName"
            FROM "OSLP" WHERE "Active" = 'Y' ORDER BY "SlpName"
            """;

        return await _hana.QueryAsync<SalesEmployeeDto>(sql, ct: ct);
    }
}
