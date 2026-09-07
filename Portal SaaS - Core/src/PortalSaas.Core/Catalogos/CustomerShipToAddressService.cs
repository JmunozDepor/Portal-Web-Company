using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Direcciones de despacho (CRD1) de un cliente -- ver ICustomerShipToAddressService.</summary>
public sealed class CustomerShipToAddressService : ICustomerShipToAddressService
{
    private readonly IHanaService _hana;

    public CustomerShipToAddressService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<string>> GetShipToAddressCodesAsync(string cardCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT "Address" AS "Code" FROM "CRD1"
            WHERE "CardCode" = :cardCode AND "AddressType" = 'S'
            """;

        var rows = await _hana.QueryAsync<AddressCodeRow>(sql, new { cardCode }, ct);
        return rows.Select(r => r.Code).ToList();
    }

    private sealed record AddressCodeRow
    {
        public string Code { get; init; } = null!;
    }
}
