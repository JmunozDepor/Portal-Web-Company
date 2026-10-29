using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Catálogo de indicadores de documento (OIDC) de la compañía SAP activa.</summary>
public sealed class DocumentIndicatorCatalogService : IDocumentIndicatorCatalogService
{
    private readonly IHanaService _hana;

    public DocumentIndicatorCatalogService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<DocumentIndicatorDto>> ListAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT "Code" AS "Code", "Name" AS "Name"
            FROM "OIDC"
            ORDER BY "Code"
            """;

        return await _hana.QueryAsync<DocumentIndicatorDto>(sql, ct: ct);
    }
}
