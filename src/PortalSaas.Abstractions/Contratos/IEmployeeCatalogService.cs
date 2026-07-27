using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Catálogo de empleados (OHEM) de la compañía SAP activa -- universo completo, a
/// diferencia de ISalesEmployeeCatalogService (OSLP, solo vendedores). Prerrequisito
/// de Modulo.Rendiciones.
/// </summary>
public interface IEmployeeCatalogService
{
    Task<IReadOnlyList<EmployeeDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default);
}
