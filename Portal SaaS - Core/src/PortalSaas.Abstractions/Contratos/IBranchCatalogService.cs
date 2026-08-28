using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de sucursales (OBPL) de la compañía SAP activa. Ver el doc-comment de IWarehouseCatalogService.ListAsync.</summary>
public interface IBranchCatalogService
{
    Task<IReadOnlyList<BranchDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default);
}
