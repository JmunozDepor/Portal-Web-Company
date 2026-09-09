namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Control de consumo de un ExternalServiceProvider concreto en su período de cuota
/// (ver Models.QuotaPeriods): por MES para los servicios de Azure, por DÍA para Google
/// Gemini. Portado de IConsumoServicioExternoService (PortalSAP_v2) -- ahí vivía en el
/// Core de la plataforma (PORTALWEB), acá se acota a la base propia del plugin y es
/// por-proveedor (cada cuenta lleva su propio contador).
///
/// El <c>quotaPeriod</c> lo resuelve quien llama (IExternalServiceProviderSelector /
/// el extractor) a partir del ServiceType -- este servicio no conoce ese mapeo, solo
/// sabe en qué "balde" de external_service_usages acumular (day = 0 mensual, 1..31
/// diario).
/// </summary>
public interface IExternalServiceUsageService
{
    /// <summary>
    /// Reserva "quantity" unidades del período actual del proveedor SI Y SOLO SI no
    /// supera periodLimit -- atómico. Usar ANTES de llamar a un servicio externo cuyo
    /// costo por invocación es fijo y conocido de antemano (ej. Azure Maps: 1
    /// transacción por request HTTP). Devuelve false sin reservar nada si hacerlo
    /// superaría el límite -- en ese caso el llamador debe probar el próximo proveedor
    /// disponible (ver IExternalServiceProviderSelector), no ejecutar la llamada.
    /// </summary>
    Task<bool> TryReserveAsync(long providerId, int quantity, int periodLimit, string quotaPeriod, CancellationToken ct = default);

    /// <summary>
    /// Suma "quantity" al consumo del período actual del proveedor sin condicionar a
    /// ningún límite -- usar DESPUÉS de una llamada cuyo costo real solo se conoce al
    /// terminar (ej. Azure Document Intelligence factura por página analizada; Gemini
    /// cuenta 1 por solicitud).
    /// </summary>
    Task RecordAsync(long providerId, int quantity, string quotaPeriod, CancellationToken ct = default);

    /// <summary>Unidades consumidas por el proveedor en el período de cuota vigente (mes o día).</summary>
    Task<int> GetCurrentUsageAsync(long providerId, string quotaPeriod, CancellationToken ct = default);
}
