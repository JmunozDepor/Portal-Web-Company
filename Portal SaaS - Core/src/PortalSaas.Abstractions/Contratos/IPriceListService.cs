namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Resuelve el precio de un artículo en una lista de precios puntual (ITM1) -- usado
/// para autocompletar el precio unitario de una línea cuando el cliente/proveedor
/// tiene una lista de precios asignada (OCRD.ListNum). Portado de IListaPrecioService
/// en referencia-original/PortalSAP_v2.
/// </summary>
public interface IPriceListService
{
    /// <summary>Null si el artículo no tiene precio definido en esa lista.</summary>
    Task<decimal?> GetPriceAsync(string itemCode, int priceList, CancellationToken ct = default);

    /// <summary>
    /// Batch: precio de varios artículos en una sola lista de precios, en una única
    /// consulta -- para no hacer una consulta por línea al importar un archivo con
    /// muchas filas (ver Módulo Importador Genérico). Los artículos sin precio
    /// definido en esa lista simplemente no aparecen en el resultado.
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> GetPricesAsync(IReadOnlyCollection<string> itemCodes, int priceList, CancellationToken ct = default);
}
