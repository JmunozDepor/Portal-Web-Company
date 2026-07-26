using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de proveedores (OCRD, CardType='S') de la compañía SAP activa -- lado proveedor de ICustomerCatalogService.</summary>
public interface ISupplierCatalogService
{
    Task<IReadOnlyList<SupplierDto>> ListAsync(SupplierFilter? filter = null, CancellationToken ct = default);

    Task<SupplierDto?> GetAsync(string cardCode, CancellationToken ct = default);
}
