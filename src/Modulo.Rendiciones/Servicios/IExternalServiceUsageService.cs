namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Control de consumo mensual de un ExternalServiceProvider concreto. Portado de
/// IConsumoServicioExternoService (PortalSAP_v2) -- ahí vivía en el Core de la
/// plataforma (PORTALWEB), acá se acota a la base propia del plugin, ahora
/// por-proveedor (no por-servicio-genérico): con múltiples cuentas configurables por
/// servicio (ver ExternalServiceProvider), cada una lleva su propio contador.
/// </summary>
public interface IExternalServiceUsageService
{
    /// <summary>
    /// Reserva "quantity" unidades del mes actual del proveedor indicado SI Y SOLO SI
    /// no supera monthlyLimit -- atómico. Usar ANTES de llamar a un servicio externo
    /// cuyo costo por invocación es fijo y conocido de antemano (ej. Azure Maps: 1
    /// transacción por request HTTP). Devuelve false sin reservar nada si hacerlo
    /// superaría el límite -- en ese caso el llamador debe probar el próximo proveedor
    /// disponible (ver IExternalServiceProviderSelector), no ejecutar la llamada.
    /// </summary>
    Task<bool> TryReserveAsync(long providerId, int quantity, int monthlyLimit, CancellationToken ct = default);

    /// <summary>
    /// Suma "quantity" al consumo del mes actual del proveedor sin condicionar a
    /// ningún límite -- usar DESPUÉS de una llamada cuyo costo real solo se conoce al
    /// terminar (ej. Azure Document Intelligence factura por página analizada).
    /// </summary>
    Task RecordAsync(long providerId, int quantity, CancellationToken ct = default);

    Task<int> GetCurrentMonthUsageAsync(long providerId, CancellationToken ct = default);
}
