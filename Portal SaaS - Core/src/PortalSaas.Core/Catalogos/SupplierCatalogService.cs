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

    public async Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> cardCodes, CancellationToken ct = default)
    {
        if (cardCodes.Count == 0)
        {
            return new Dictionary<string, bool>();
        }

        var parameters = new Dictionary<string, object?>();
        var placeholders = new List<string>();
        var i = 0;
        foreach (var cardCode in cardCodes)
        {
            var name = $"cardCode{i++}";
            placeholders.Add($":{name}");
            parameters[name] = cardCode;
        }

        var sql = $"""
            SELECT "CardCode" AS "CardCode", "validFor" AS "ValidFor" FROM "OCRD"
            WHERE "CardType" = 'S' AND "CardCode" IN ({string.Join(",", placeholders)})
            """;

        var rows = await _hana.QueryAsync<ActiveStatusRow>(sql, parameters, ct);
        return rows.ToDictionary(r => r.CardCode, r => string.Equals(r.ValidFor, "Y", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ActiveStatusRow
    {
        public string CardCode { get; init; } = null!;
        public string ValidFor { get; init; } = null!;
    }
}
