using System.Text.Json;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Geocodifica origen/destino (Azure Maps Search API) y calcula la distancia de ruta
/// entre esos dos puntos (Azure Maps Route Directions API) -- dos llamados HTTP, la API
/// de rutas no acepta direcciones en texto directamente, solo coordenadas.
///
/// countrySet=CL fijo en el geocoding (heredado del original, cliente que opera en
/// Chile) -- generalizar/configurar por organización cuando el plugin soporte otro
/// país, ver PENDIENTE.md.
///
/// Credenciales resueltas 100% self-service vía IExternalServiceProviderSelector
/// (Configuracion &gt; Proveedores) -- puede haber más de una cuenta de Azure Maps
/// configurada por compañía; si la de mayor prioridad ya agotó su cuota mensual, se
/// prueba automáticamente la siguiente antes de dar el servicio por no disponible.
/// Cada llamado HTTP a Azure Maps (geocoding o ruta) es 1 transacción facturable -- se
/// reserva 1 unidad ANTES de cada llamado real (no 3 de una al arrancar el método) --
/// si geocodificar el origen ya falla, no tiene sentido haber reservado cupo para
/// destino/ruta que nunca se van a llamar.
/// </summary>
public sealed class AzureMapsRoutingService : IRoutingService
{
    private const string BaseUrl = "https://atlas.microsoft.com/";

    private readonly HttpClient _http;
    private readonly IExternalServiceProviderSelector _selector;
    private readonly ILogger<AzureMapsRoutingService> _logger;

    public AzureMapsRoutingService(HttpClient http, IExternalServiceProviderSelector selector, ILogger<AzureMapsRoutingService> logger)
    {
        _http = http;
        _http.BaseAddress ??= new Uri(BaseUrl);
        _selector = selector;
        _logger = logger;
    }

    public async Task<RouteResultDto> CalculateDistanceAsync(Guid companyId, string origin, string destination, CancellationToken ct = default)
    {
        const string noProvidersMessage = "No hay ninguna cuenta de Azure Maps configurada, o todas alcanzaron su límite mensual gratuito -- configurá una en Configuración > Proveedores.";

        try
        {
            var originProvider = await _selector.SelectForReservationAsync(companyId, ExternalServiceType.AzureMaps, 1, ct);
            if (originProvider is null)
                return new RouteResultDto(null, noProvidersMessage);

            var originPoint = await GeocodeAsync(originProvider, origin, ct);
            if (originPoint is null)
                return new RouteResultDto(null, $"No se pudo ubicar el origen \"{origin}\".");

            var destinationProvider = await _selector.SelectForReservationAsync(companyId, ExternalServiceType.AzureMaps, 1, ct);
            if (destinationProvider is null)
                return new RouteResultDto(null, noProvidersMessage);

            var destinationPoint = await GeocodeAsync(destinationProvider, destination, ct);
            if (destinationPoint is null)
                return new RouteResultDto(null, $"No se pudo ubicar el destino \"{destination}\".");

            var routeProvider = await _selector.SelectForReservationAsync(companyId, ExternalServiceType.AzureMaps, 1, ct);
            if (routeProvider is null)
                return new RouteResultDto(null, noProvidersMessage);

            var query = FormattableString.Invariant(
                $"{originPoint.Value.Lat},{originPoint.Value.Lon}:{destinationPoint.Value.Lat},{destinationPoint.Value.Lon}");
            var url = $"route/directions/json?api-version=1.0&subscription-key={routeProvider.ApiKey}&query={Uri.EscapeDataString(query)}";

            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return new RouteResultDto(null, $"Azure Maps devolvió {(int)response.StatusCode} al calcular la ruta.");

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var routes = document.RootElement.GetProperty("routes");
            if (routes.GetArrayLength() == 0)
                return new RouteResultDto(null, "Azure Maps no encontró una ruta entre esas dos direcciones.");

            var meters = routes[0].GetProperty("summary").GetProperty("lengthInMeters").GetInt32();
            return new RouteResultDto(Math.Round(meters / 1000m, 2), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo calcular la ruta con Azure Maps ({Origin} -> {Destination}).", origin, destination);
            return new RouteResultDto(null, "No se pudo calcular la ruta automáticamente -- probá con una dirección más específica.");
        }
    }

    private async Task<(double Lat, double Lon)?> GeocodeAsync(SelectedProvider provider, string address, CancellationToken ct)
    {
        var url = $"search/address/json?api-version=1.0&subscription-key={provider.ApiKey}&countrySet=CL&limit=1&query={Uri.EscapeDataString(address)}";
        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var results = document.RootElement.GetProperty("results");
        if (results.GetArrayLength() == 0)
            return null;

        var position = results[0].GetProperty("position");
        return (position.GetProperty("lat").GetDouble(), position.GetProperty("lon").GetDouble());
    }
}
