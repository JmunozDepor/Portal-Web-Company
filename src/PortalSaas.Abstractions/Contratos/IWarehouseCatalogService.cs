using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Catálogo de almacenes (OWHS) de la compañía SAP activa.</summary>
public interface IWarehouseCatalogService
{
    Task<IReadOnlyList<WarehouseDto>> ListAsync(CancellationToken ct = default);
}
