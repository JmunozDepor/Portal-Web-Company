using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Stock por almacén (OITW) de un artículo puntual, para el visor Maestro de Producto.</summary>
public interface IItemStockService
{
    /// <summary>Lista vacía si el artículo no tiene registro en ningún almacén.</summary>
    Task<IReadOnlyList<WarehouseStockDto>> GetStockByItemAsync(string itemCode, CancellationToken ct = default);

    /// <summary>
    /// Disponible (OnHand - IsCommited) de varios pares (Artículo, Bodega) en una sola
    /// consulta -- usado por GenericImportValidationRuleEngine.StockAvailableRule para no
    /// consultar una vez por línea al importar un archivo con muchas filas. Un par sin
    /// registro en OITW no aparece en el resultado (disponible implícito 0).
    /// </summary>
    Task<IReadOnlyDictionary<(string ItemCode, string WhsCode), decimal>> GetAvailableStockAsync(
        IReadOnlyList<(string ItemCode, string WhsCode)> pairs, CancellationToken ct = default);
}
