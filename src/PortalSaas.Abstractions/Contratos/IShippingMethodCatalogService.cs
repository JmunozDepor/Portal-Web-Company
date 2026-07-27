using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de métodos de envío (OSHP) de la compañía SAP activa -- tab Logística.</summary>
public interface IShippingMethodCatalogService
{
    Task<IReadOnlyList<ShippingMethodDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default);
}
