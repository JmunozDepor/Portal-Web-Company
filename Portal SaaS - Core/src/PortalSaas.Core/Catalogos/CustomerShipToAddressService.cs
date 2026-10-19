using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

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
            WHERE "CardCode" = :cardCode AND "AdresType" = 'S'
            """;

        var rows = await _hana.QueryAsync<AddressCodeRow>(sql, new { cardCode }, ct);
        return rows.Select(r => r.Code).ToList();
    }

    public async Task<IReadOnlyList<CustomerShipToAddressDto>> ListShipToAddressesAsync(string cardCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT "Address" AS "Code", "Street" AS "Street", "Block" AS "Block", "City" AS "City", "County" AS "County"
            FROM "CRD1"
            WHERE "CardCode" = :cardCode AND "AdresType" = 'S'
            ORDER BY "Address"
            """;

        return await _hana.QueryAsync<CustomerShipToAddressDto>(sql, new { cardCode }, ct);
    }

    private sealed record AddressCodeRow
    {
        public string Code { get; init; } = null!;
    }
}
