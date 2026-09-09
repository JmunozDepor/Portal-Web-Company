using System.Net;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Implementación de <see cref="IExternalServiceHealthChecker"/> -- ver esa interfaz para
/// el porqué. Gemini/Azure Maps van por un typed HttpClient propio (AddHttpClient) con
/// timeout corto; Azure Document Intelligence usa EL MISMO SDK que el extractor real
/// (<see cref="AzureDocumentIntelligenceExtractorService"/>), así "Probar OK" garantiza
/// que el OCR se va a poder autenticar igual.
/// </summary>
public sealed class ExternalServiceHealthChecker : IExternalServiceHealthChecker
{
    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/";

    private readonly IExternalServiceProviderService _providers;
    private readonly ISecretoCifradoService _secretos;
    private readonly HttpClient _http;
    private readonly ILogger<ExternalServiceHealthChecker> _logger;
    private readonly DocumentIntelligenceClientOptions? _azureOptions;

    public ExternalServiceHealthChecker(IExternalServiceProviderService providers, ISecretoCifradoService secretos,
        HttpClient http, ILogger<ExternalServiceHealthChecker> logger, DocumentIntelligenceClientOptions? azureOptions = null)
    {
        _providers = providers;
        _secretos = secretos;
        _http = http;
        _logger = logger;
        _azureOptions = azureOptions;
    }

    public async Task<ServiceHealthResult> CheckAsync(long providerId, Guid companyId, CancellationToken ct = default)
    {
        var provider = await _providers.GetAsync(providerId, companyId, ct);
        if (provider is null)
            return ServiceHealthResult.Unhealthy("El proveedor no existe o no pertenece a esta compañía.");

        string apiKey;
        try
        {
            apiKey = _secretos.Decrypt(provider.ApiKeyEncrypted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo descifrar la clave del proveedor {ProviderId}.", providerId);
            return ServiceHealthResult.Unhealthy("La clave guardada no se pudo descifrar -- volvé a ingresarla y guardá.");
        }

        // Un salto de línea o espacios al final (típico al pegar) hacen que Azure/Google
        // devuelvan 401/400 aunque la clave sea correcta -- se limpian acá.
        apiKey = apiKey.Trim();
        if (apiKey.Length == 0)
            return ServiceHealthResult.Unhealthy("El proveedor no tiene clave configurada.");

        try
        {
            return provider.ServiceType switch
            {
                ExternalServiceType.GoogleGeminiVision => await CheckGeminiAsync(provider, apiKey, ct),
                ExternalServiceType.AzureDocumentIntelligence => await CheckAzureDocIntelAsync(provider, apiKey, ct),
                ExternalServiceType.AzureMaps => await CheckAzureMapsAsync(apiKey, ct),
                _ => ServiceHealthResult.Unhealthy($"Tipo de servicio sin verificación disponible: {provider.ServiceType}."),
            };
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ServiceHealthResult.Unhealthy("La verificación superó el tiempo de espera -- revisá la conectividad de red hacia el proveedor.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Fallo de red al verificar el proveedor {ProviderId} ({ServiceType}).", providerId, provider.ServiceType);
            return ServiceHealthResult.Unhealthy($"No se pudo contactar al proveedor: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error inesperado al verificar el proveedor {ProviderId} ({ServiceType}).", providerId, provider.ServiceType);
            return ServiceHealthResult.Unhealthy($"No se pudo completar la verificación: {ex.Message}");
        }
    }

    /// <summary>
    /// GET del modelo (no <c>generateContent</c>) -- valida la clave y que el id del modelo
    /// del campo Endpoint exista, sin gastar una solicitud del cupo diario.
    /// </summary>
    private async Task<ServiceHealthResult> CheckGeminiAsync(ExternalServiceProvider provider, string apiKey, CancellationToken ct)
    {
        // Mismo criterio que el extractor real: tolera que el campo Endpoint traiga la
        // URL completa o "models/xxx:generateContent" pegado por error.
        var model = GeminiReceiptExtractorService.ResolveModel(provider.Endpoint);

        using var response = await _http.GetAsync(
            $"{GeminiBaseUrl}v1beta/models/{Uri.EscapeDataString(model)}?key={apiKey}", ct);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var soportaGenerate = doc.RootElement.TryGetProperty("supportedGenerationMethods", out var methods)
                && methods.ValueKind == JsonValueKind.Array
                && methods.EnumerateArray().Any(m => m.GetString() == "generateContent");

            return soportaGenerate
                ? ServiceHealthResult.Healthy($"OK -- clave válida y el modelo «{model}» acepta generateContent.")
                : ServiceHealthResult.Unhealthy($"La clave es válida pero el modelo «{model}» no soporta generateContent -- poné otro id de modelo en Endpoint (ej. gemini-2.5-flash).");
        }

        var detalle = await SafeReadBodyAsync(response, ct);
        return response.StatusCode switch
        {
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized =>
                ServiceHealthResult.Unhealthy($"Google rechazó la clave (HTTP {(int)response.StatusCode}) -- verificá que sea una API key de Google AI Studio y que esté completa. {detalle}"),
            HttpStatusCode.BadRequest =>
                ServiceHealthResult.Unhealthy($"Google rechazó la solicitud (HTTP 400) -- el campo Endpoint debe ser solo el id del modelo (ej. «gemini-2.5-flash») o quedar vacío, no una URL. {detalle}"),
            HttpStatusCode.NotFound =>
                ServiceHealthResult.Unhealthy($"El modelo «{model}» no existe -- revisá el id en el campo Endpoint. {detalle}"),
            HttpStatusCode.TooManyRequests =>
                ServiceHealthResult.Unhealthy("Google está limitando las solicitudes ahora mismo -- la clave parece válida, reintentá en unos segundos."),
            _ => ServiceHealthResult.Unhealthy($"Google respondió HTTP {(int)response.StatusCode}. {detalle}"),
        };
    }

    /// <summary>
    /// Valida endpoint + clave con el <b>mismo SDK</b> (<c>Azure.AI.DocumentIntelligence</c>)
    /// y la misma vía de autenticación (<see cref="AzureKeyCredential"/>) que el extractor
    /// real. <c>GetResourceDetails</c> / <c>GetModel("prebuilt-invoice")</c> son GET de
    /// metadata: no analizan ninguna página, no consumen cuota. Se prueban los dos porque
    /// algunos recursos gatean <c>/info</c> distinto que la metadata de modelo.
    /// </summary>
    private async Task<ServiceHealthResult> CheckAzureDocIntelAsync(ExternalServiceProvider provider, string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(provider.Endpoint))
            return ServiceHealthResult.Unhealthy("El proveedor de Azure Document Intelligence no tiene endpoint configurado.");

        Uri endpoint;
        try
        {
            endpoint = new Uri(provider.Endpoint.Trim(), UriKind.Absolute);
        }
        catch (UriFormatException)
        {
            return ServiceHealthResult.Unhealthy("El endpoint no es una URL válida -- debe ser la URL del recurso (https://<nombre>.cognitiveservices.azure.com/).");
        }

        var admin = new DocumentIntelligenceAdministrationClient(
            endpoint, new AzureKeyCredential(apiKey), _azureOptions ?? new DocumentIntelligenceClientOptions());

        try
        {
            try
            {
                await admin.GetResourceDetailsAsync(ct);
            }
            catch (RequestFailedException ex) when (ex.Status is 401 or 403 or 404 or 405)
            {
                // /info gateado distinto en algunos recursos -- reintentar con la metadata
                // del modelo que el OCR realmente usa.
                await admin.GetModelAsync("prebuilt-invoice", ct);
            }

            return ServiceHealthResult.Healthy("OK -- endpoint y clave válidos (verificado con el mismo SDK que usa el OCR).");
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex, "Azure Document Intelligence rechazó la verificación (HTTP {Status}, {Code}).", ex.Status, ex.ErrorCode);
            return ex.Status switch
            {
                401 or 403 => ServiceHealthResult.Unhealthy(
                    $"Azure rechazó la clave (HTTP {ex.Status}{(ex.ErrorCode is null ? "" : $", {ex.ErrorCode}")}). " +
                    "La misma clave y endpoint que funcionaban antes deberían servir -- revisá que estén en los campos correctos " +
                    "(Endpoint = URL del recurso; Clave = Key 1 o Key 2 de «Keys and Endpoint») y que el recurso no tenga " +
                    "deshabilitada la autenticación por clave."),
                404 => ServiceHealthResult.Unhealthy(
                    "El endpoint no corresponde a un recurso de Document Intelligence / Form Recognizer -- usá la URL de «Keys and Endpoint» del recurso."),
                _ => ServiceHealthResult.Unhealthy($"Azure respondió HTTP {ex.Status}. {ex.ErrorCode ?? ex.Message}"),
            };
        }
    }

    /// <summary>
    /// Azure Maps no tiene endpoint de metadata gratis -- se hace una búsqueda mínima
    /// (1 transacción del cupo). Se avisa en el mensaje.
    /// </summary>
    private async Task<ServiceHealthResult> CheckAzureMapsAsync(string apiKey, CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            $"https://atlas.microsoft.com/search/address/json?api-version=1.0&subscription-key={apiKey}&query=Santiago", ct);

        return response.IsSuccessStatusCode
            ? ServiceHealthResult.Healthy("OK -- clave válida (consumió 1 transacción de búsqueda).")
            : ServiceHealthResult.Unhealthy($"Azure Maps respondió HTTP {(int)response.StatusCode} -- revisá la clave.");
    }

    /// <summary>Cuerpo de error recortado -- no volcar un JSON/HTML largo del proveedor dentro de un alert.</summary>
    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = (await response.Content.ReadAsStringAsync(ct)).Trim();
            if (body.Length == 0)
                return string.Empty;
            return body.Length > 300 ? body[..300] + "…" : body;
        }
        catch
        {
            return string.Empty;
        }
    }
}
