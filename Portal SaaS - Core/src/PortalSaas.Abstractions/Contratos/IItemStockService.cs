using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Stock por almacén (OITW) de un artículo puntual, para el visor Maestro de Producto.</summary>
public interface IItemStockService
{
    /// <summary>Lista vacía si el artículo no tiene registro en ningún almacén.</summary>
    Task<IReadOnlyList<WarehouseStockDto>> GetStockByItemAsync(string itemCode, CancellationToken ct = default);
}
