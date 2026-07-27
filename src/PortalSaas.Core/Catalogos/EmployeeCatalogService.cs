using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de empleados (OHEM) de la compañía SAP activa.</summary>
public sealed class EmployeeCatalogService : IEmployeeCatalogService
{
    private readonly IHanaService _hana;

    public EmployeeCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<EmployeeDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
    {
        var (whereSql, limitSql, parameters) = CatalogSqlHelper.BuildSearchFilter(
            searchText, ["TO_VARCHAR(\"empID\")", "\"firstName\"", "\"lastName\""], limit, fixedWhere: "\"Active\" = 'Y'");

        var sql = $"""
            SELECT "empID" AS "EmployeeCode", "firstName" || ' ' || "lastName" AS "EmployeeName"
            FROM "OHEM" WHERE {whereSql}
            ORDER BY "lastName"
            {limitSql}
            """;

        return await _hana.QueryAsync<EmployeeDto>(sql, parameters, ct);
    }
}
