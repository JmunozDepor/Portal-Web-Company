using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de sucursales (OBPL) de la compañía SAP activa.</summary>
public sealed class BranchCatalogService : IBranchCatalogService
{
    private readonly IHanaService _hana;

    public BranchCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<BranchDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        // TO_VARCHAR sobre BPLId (numérico) para poder buscarlo como texto -- mismo
        // criterio que AlmacenCatalogoService del original con WhsCode.
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["TO_VARCHAR(\"BPLId\")", "\"BPLName\""], limit, fixedWhere: "\"Disabled\" = 'N'");

        var sql = $"""
            SELECT "BPLId" AS "BranchCode", "BPLName" AS "BranchName"
            FROM "OBPL" WHERE {whereSql}
            ORDER BY "BPLName"
            {limitSql}
            """;

        return await _hana.QueryAsync<BranchDto>(sql, parameters, ct);
    }
}
