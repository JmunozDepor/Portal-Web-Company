using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Catálogo de almacenes (OWHS) de la compañía SAP activa. searchText/limit opcionales
/// -- sin buscar, listado completo (precarga de un &lt;select&gt;); buscando, SIEMPRE
/// topado (ver CatalogSqlHelper.BuildSearchFilter en el Core).
/// </summary>
public interface IWarehouseCatalogService
{
    Task<IReadOnlyList<WarehouseDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default);
}
