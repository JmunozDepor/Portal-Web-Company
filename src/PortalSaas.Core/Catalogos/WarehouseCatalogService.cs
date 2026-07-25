using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de almacenes (OWHS) de la compañía SAP activa.</summary>
public sealed class WarehouseCatalogService : IWarehouseCatalogService
{
    private readonly IHanaService _hana;

    public WarehouseCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<WarehouseDto>> ListAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT "WhsCode" AS "WarehouseCode", "WhsName" AS "WarehouseName"
            FROM "OWHS" WHERE "Inactive" = 'N' ORDER BY "WhsName"
            """;

        return await _hana.QueryAsync<WarehouseDto>(sql, ct: ct);
    }
}
