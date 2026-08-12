namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Calcula la distancia de ruta (no línea recta) entre dos direcciones -- detrás de una
/// interfaz para poder cambiar de proveedor sin tocar IExpenseService (mismo criterio
/// que IReceiptExtractorService/IAttachmentStorageService). Implementación actual:
/// Azure Maps (geocoding + Route Directions API).
/// </summary>
public interface IRoutingService
{
    /// <summary>Nunca lanza -- si no puede geocodificar alguna dirección o el proveedor falla, devuelve el DTO con Error seteado.</summary>
    Task<RouteResultDto> CalculateDistanceAsync(Guid companyId, string origin, string destination, CancellationToken ct = default);
}
