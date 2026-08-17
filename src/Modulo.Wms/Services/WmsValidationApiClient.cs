using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Modulo.Wms.Services;

/// <summary>
/// Cliente de LGFAPI (Oracle WMS Cloud, endpoint de consulta de status -- distinto de
/// init_stage_interface, que solo confirma recepción). Puerto directo de
/// WmsValidationApiClient del legado (WMS_Suite, fuera de este repo), mismo formato de
/// consulta/respuesta -- no rediseñado. Ver
/// docs/superpowers/specs/2026-08-17-batching-y-reconciliacion-wms-design.md.
/// </summary>
public class WmsValidationApiClient : IWmsValidationApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WmsValidationApiClient> _logger;

    public WmsValidationApiClient(HttpClient httpClient, ILogger<WmsValidationApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<WmsStageCheckResult> CheckStageRecordAsync(
        string lgfApiBaseUrl, string usuario, string clave,
        string entity, string keyField, string keyValue, string? companyCode,
        bool filtrarPorUrl, CancellationToken ct = default)
    {
        var baseUrl = lgfApiBaseUrl.EndsWith('/') ? lgfApiBaseUrl : lgfApiBaseUrl + "/";
        var url = $"{baseUrl}{entity}?{keyField}={Uri.EscapeDataString(keyValue)}";
        if (filtrarPorUrl && !string.IsNullOrWhiteSpace(companyCode))
        {
            url += $"&company_code={Uri.EscapeDataString(companyCode)}";
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var authToken = Encoding.UTF8.GetBytes($"{usuario}:{clave}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

            var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WMS LGFAPI [{Entity}] respondió {Status} para {KeyField}={KeyValue}: {Body}", entity, response.StatusCode, keyField, keyValue, body);
                return new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null);
            }

            var parsed = JsonSerializer.Deserialize<LgfApiListResponse>(body);
            if (parsed is null || parsed.Results.Count == 0)
            {
                return new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null);
            }

            foreach (var fila in parsed.Results)
            {
                if (!MatchesCompany(fila, companyCode))
                {
                    continue;
                }

                return new WmsStageCheckResult(
                    Found: true,
                    StatusId: ReadIntOrNull(fila, "status_id"),
                    ErrorMessage: ReadStringOrNull(fila, "error_message"));
            }

            return new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando WMS LGFAPI [{Entity}] para {KeyField}={KeyValue}", entity, keyField, keyValue);
            return new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null);
        }
    }

    private static bool MatchesCompany(JsonElement fila, string? companyCode)
    {
        if (string.IsNullOrWhiteSpace(companyCode))
        {
            return true;
        }

        foreach (var campo in new[] { "company_code", "company_id", "parent_company_id" })
        {
            var valor = ReadStringOrNull(fila, campo);
            if (valor is not null && string.Equals(valor, companyCode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string? ReadStringOrNull(JsonElement fila, string campo) =>
        fila.TryGetProperty(campo, out var valor) && valor.ValueKind != JsonValueKind.Null ? valor.ToString() : null;

    private static int? ReadIntOrNull(JsonElement fila, string campo) =>
        fila.TryGetProperty(campo, out var valor) && valor.ValueKind == JsonValueKind.Number ? valor.GetInt32() : null;

    private sealed class LgfApiListResponse
    {
        [JsonPropertyName("result_count")]
        public int ResultCount { get; set; }
        [JsonPropertyName("next_page")]
        public string? NextPage { get; set; }
        [JsonPropertyName("results")]
        public List<JsonElement> Results { get; set; } = [];
    }
}
