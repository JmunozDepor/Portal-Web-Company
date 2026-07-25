using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de clientes (OCRD, CardType='C') de la compañía SAP activa.</summary>
public interface ICustomerCatalogService
{
    Task<IReadOnlyList<CustomerDto>> ListAsync(CustomerFilter? filter = null, CancellationToken ct = default);

    Task<CustomerDto?> GetAsync(string cardCode, CancellationToken ct = default);
}
