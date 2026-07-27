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
        // Ver el comentario equivalente en CustomerCatalogService.ListAsync.
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            filter?.SearchText, ["\"CardCode\"", "\"CardName\""], filter?.Limit, fixedWhere: "\"CardType\" = 'S'");

        var sql = $"""
            SELECT "CardCode", "CardName"
            FROM "OCRD" WHERE {whereSql}
            ORDER BY "CardName"
            {limitSql}
            """;

        return await _hana.QueryAsync<SupplierDto>(sql, parameters, ct);
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
