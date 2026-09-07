using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Catálogo de artículos (OITM) de la compañía SAP activa -- solo búsqueda, nunca
/// listado completo (la tabla real puede tener cientos de miles de filas).
/// </summary>
public interface IItemCatalogService
{
    Task<IReadOnlyList<ItemDto>> SearchAsync(string text, int limit = 30, CancellationToken ct = default);

    /// <summary>
    /// Resuelve un conjunto acotado de códigos puntuales en una sola consulta (nunca
    /// listado completo) -- usado por Modulo.ImportacionGenerica para cruzar todos los
    /// ItemCode distintos de un archivo de una vez, en vez de una búsqueda por fila.
    /// </summary>
    Task<IReadOnlyList<ItemDto>> GetByCodesAsync(IReadOnlyCollection<string> itemCodes, CancellationToken ct = default);

    /// <summary>
    /// Top N artículos por nombre, SIN filtro de texto -- excepción deliberada al "solo
    /// búsqueda" de la clase (acotado por `limit`, nunca listado completo real). Pensado
    /// para el atajo "*" del visor Maestro de Producto (ver
    /// Modulo.Inventario/Pages/ProductMaster/Index.cshtml.cs) -- ningún otro consumidor
    /// lo usa hoy, Ventas/Compras siguen exigiendo texto real vía SearchAsync.
    /// </summary>
    Task<IReadOnlyList<ItemDto>> GetTopAsync(int limit = 30, CancellationToken ct = default);

    /// <summary>ItemCode -> validFor == "Y" en OITM, para varios códigos en una sola consulta -- usado por la regla ItemActiveInSap.</summary>
    Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> itemCodes, CancellationToken ct = default);
}
