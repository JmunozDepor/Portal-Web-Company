using PortalSaas.Abstractions.Modelos;

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

    /// <summary>
    /// Catálogo completo de listas de precios (OPLN) de la compañía activa -- a
    /// diferencia de OITM/artículo, OPLN tiene pocas filas (unas pocas a unas
    /// decenas), se lista completa sin búsqueda ni límite, para poblar un combo (ver
    /// visor Maestro de Producto).
    /// </summary>
    Task<IReadOnlyList<PriceListOptionDto>> ListAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Precio especial del socio por artículo, vigente a <paramref name="date"/>: primero el
    /// descuento por período (SPP1), si no el precio especial general (OSPP, Valid = 'Y').
    /// Artículos sin precio especial no vienen en el resultado. No considera descuentos por
    /// volumen (SPP2) ni grupos de descuento (OEDG). Implementación por defecto para no
    /// obligar a los dobles de test.
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> GetSpecialPricesAsync(string cardCode, IReadOnlyCollection<string> itemCodes, DateTime date,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Este servicio no soporta precios especiales.");

    /// <summary>
    /// Igual que <see cref="GetPricesAsync"/> pero con la moneda de cada precio (ITM1.Currency)
    /// -- los formularios genéricos la postean como moneda de la línea para que SAP convierta
    /// a la moneda del documento. Implementación por defecto sin moneda, para los dobles de test.
    /// </summary>
    async Task<IReadOnlyDictionary<string, ItemPriceQuote>> GetPriceQuotesAsync(IReadOnlyCollection<string> itemCodes, int priceList,
        CancellationToken ct = default) =>
        (await GetPricesAsync(itemCodes, priceList, ct)).ToDictionary(p => p.Key, p => new ItemPriceQuote(p.Value, null), StringComparer.OrdinalIgnoreCase);

    /// <summary>Igual que <see cref="GetSpecialPricesAsync"/> pero con la moneda de cada precio especial (SPP1/OSPP.Currency).</summary>
    async Task<IReadOnlyDictionary<string, ItemPriceQuote>> GetSpecialPriceQuotesAsync(string cardCode, IReadOnlyCollection<string> itemCodes, DateTime date,
        CancellationToken ct = default) =>
        (await GetSpecialPricesAsync(cardCode, itemCodes, date, ct)).ToDictionary(p => p.Key, p => new ItemPriceQuote(p.Value, null), StringComparer.OrdinalIgnoreCase);
}

/// <summary>Precio + moneda en que está expresado (null = moneda desconocida, SAP asume la del documento).</summary>
public sealed record ItemPriceQuote(decimal Price, string? Currency);
