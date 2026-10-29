using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de indicadores de documento (OIDC) de la compañía SAP activa -- documento tributario de las facturas de proveedor.</summary>
public interface IDocumentIndicatorCatalogService
{
    Task<IReadOnlyList<DocumentIndicatorDto>> ListAsync(CancellationToken ct = default);
}
