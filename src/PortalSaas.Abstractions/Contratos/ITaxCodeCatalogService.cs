using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de códigos de impuesto (OVTG) de la compañía SAP activa.</summary>
public interface ITaxCodeCatalogService
{
    Task<IReadOnlyList<TaxCodeDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default);
}
