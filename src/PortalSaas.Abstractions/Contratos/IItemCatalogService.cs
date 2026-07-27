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
}
