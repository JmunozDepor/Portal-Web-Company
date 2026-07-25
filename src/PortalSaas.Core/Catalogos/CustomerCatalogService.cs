using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de clientes (OCRD, CardType='C') de la compañía SAP activa.</summary>
public sealed class CustomerCatalogService : ICustomerCatalogService
{
    private readonly IHanaService _hana;

    public CustomerCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<CustomerDto>> ListAsync(CustomerFilter? filter = null, CancellationToken ct = default)
    {
        var searchText = filter?.SearchText?.Trim();

        const string baseSql = """
            SELECT "CardCode", "CardName", "ListNum" AS "PriceListCode", "GroupNum" AS "PaymentTermsCode"
            FROM "OCRD" WHERE "CardType" = 'C'
            """;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return await _hana.QueryAsync<CustomerDto>(baseSql + " ORDER BY \"CardName\"", ct: ct);
        }

        var sql = baseSql + " AND (\"CardCode\" LIKE :texto OR \"CardName\" LIKE :texto) ORDER BY \"CardName\"";
        return await _hana.QueryAsync<CustomerDto>(sql, new { texto = $"%{searchText}%" }, ct);
    }

    public async Task<CustomerDto?> GetAsync(string cardCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT "CardCode", "CardName", "ListNum" AS "PriceListCode", "GroupNum" AS "PaymentTermsCode"
            FROM "OCRD" WHERE "CardType" = 'C' AND "CardCode" = :cardCode
            """;

        var results = await _hana.QueryAsync<CustomerDto>(sql, new { cardCode }, ct);
        return results.FirstOrDefault();
    }
}
