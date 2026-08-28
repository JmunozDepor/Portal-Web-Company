using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Paridad SKU-cliente ↔ artículo SAP (recurso estándar de Service Layer
/// AlternateCatNum) -- portado de IParidadCatalogoService en
/// referencia-original/PortalSAP_v2.
/// </summary>
public interface IItemCrossReferenceService
{
    Task<IReadOnlyList<ItemCrossReferenceDto>> ListAsync(string customerCardCode, CancellationToken ct = default);

    /// <summary>
    /// Reemplaza por completo la paridad del cliente (borra todo lo existente y crea
    /// lo nuevo -- mismo criterio que la referencia, evita diffing; el volumen por
    /// cliente es chico). Devuelve (eliminados, creados).
    /// </summary>
    Task<(int Deleted, int Created)> SyncAsync(string customerCardCode, IReadOnlyList<ItemCrossReferenceDto> items, CancellationToken ct = default);
}
