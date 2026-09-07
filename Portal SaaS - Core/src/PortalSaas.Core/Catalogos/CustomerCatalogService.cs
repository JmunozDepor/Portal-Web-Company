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
        // Sin SearchText: listado completo (ej. precarga de un <select> chico). Con
        // SearchText: buscador en vivo, SIEMPRE topado -- mismo criterio que
        // ItemCatalogService.SearchAsync, ver CatalogSqlHelper.
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            filter?.SearchText, ["\"CardCode\"", "\"CardName\""], filter?.Limit, fixedWhere: "\"CardType\" = 'C'");

        var sql = $"""
            SELECT "CardCode", "CardName", "ListNum" AS "PriceListCode", "GroupNum" AS "PaymentTermsCode"
            FROM "OCRD" WHERE {whereSql}
            ORDER BY "CardName"
            {limitSql}
            """;

        return await _hana.QueryAsync<CustomerDto>(sql, parameters, ct);
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
            WHERE "CardType" = 'C' AND "CardCode" IN ({string.Join(",", placeholders)})
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
