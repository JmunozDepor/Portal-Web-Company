using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de proveedores (OCRD, CardType='S') de la compañía SAP activa -- lado proveedor de CustomerCatalogService.</summary>
public sealed class SupplierCatalogService : ISupplierCatalogService
{
    private readonly IHanaService _hana;

    public SupplierCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<SupplierDto>> ListAsync(SupplierFilter? filter = null, CancellationToken ct = default)
    {
        var searchText = filter?.SearchText?.Trim();

        const string baseSql = """
            SELECT "CardCode", "CardName"
            FROM "OCRD" WHERE "CardType" = 'S'
            """;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return await _hana.QueryAsync<SupplierDto>(baseSql + " ORDER BY \"CardName\"", ct: ct);
        }

        var sql = baseSql + " AND (\"CardCode\" LIKE :texto OR \"CardName\" LIKE :texto) ORDER BY \"CardName\"";
        return await _hana.QueryAsync<SupplierDto>(sql, new { texto = $"%{searchText}%" }, ct);
    }

    public async Task<SupplierDto?> GetAsync(string cardCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT "CardCode", "CardName"
            FROM "OCRD" WHERE "CardType" = 'S' AND "CardCode" = :cardCode
            """;

        var results = await _hana.QueryAsync<SupplierDto>(sql, new { cardCode }, ct);
        return results.FirstOrDefault();
    }
}
