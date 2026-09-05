using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Ficha completa de un artículo puntual (OITM + OITB) para el visor Maestro de
/// Producto -- a diferencia de IItemCatalogService (búsqueda en vivo, resultados
/// acotados), este servicio siempre resuelve UN artículo por su código exacto.
/// </summary>
public interface IItemMasterDetailService
{
    /// <summary>Null si el artículo no existe.</summary>
    Task<ItemMasterDetailDto?> GetDetailAsync(string itemCode, CancellationToken ct = default);
}
