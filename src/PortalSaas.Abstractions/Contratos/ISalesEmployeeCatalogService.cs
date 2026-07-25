using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de vendedores (OSLP) de la compañía SAP activa.</summary>
public interface ISalesEmployeeCatalogService
{
    Task<IReadOnlyList<SalesEmployeeDto>> ListAsync(CancellationToken ct = default);
}
