using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Correo;

/// <summary>
/// Envía correo vía Microsoft Graph (`sendMail`), autenticado con el flujo
/// "client credentials" de Microsoft Entra ID (aplicación con permiso de aplicación
/// `Mail.Send`, sin usuario interactivo) -- estándar recomendado por Microsoft en vez
/// de SMTP con autenticación básica (deprecado). NO verificado todavía contra un
/// tenant real (ver docs/06-...md §7, a diferencia de GoogleWorkspaceEmailSender que
/// ya se probó de punta a punta) -- implementado siguiendo la documentación oficial,
/// sin un tenant de Microsoft 365 de prueba disponible todavía.
/// </summary>
internal sealed class Microsoft365EmailSender
{
    private readonly HttpClient _httpClient;

    public Microsoft365EmailSender(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task SendAsync(EmailSettings settings, Microsoft365ProviderConfig config, EmailMessage message, CancellationToken ct)
    {
        var accessToken = await AcquireAppOnlyTokenAsync(config, ct);

        var json = MicrosoftGraphPayloadBuilder.BuildSendMailJson(settings, message);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(settings.SenderEmail)}/sendMail")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, ct);
        await response.EnsureSuccessWithBodyAsync(ct);
    }

    private async Task<string> AcquireAppOnlyTokenAsync(Microsoft365ProviderConfig config, CancellationToken ct)
    {
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId,
            ["client_secret"] = config.ClientSecret,
            ["scope"] = "https://graph.microsoft.com/.default",
            ["grant_type"] = "client_credentials",
        });

        using var response = await _httpClient.PostAsync(
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(config.TenantId)}/oauth2/v2.0/token",
            tokenRequest, ct);
        await response.EnsureSuccessWithBodyAsync(ct);

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Respuesta de token de Microsoft vacía o inválida.");

        return body.AccessToken;
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}

/// <summary>
/// Forma del JSON descifrado de EmailSettings.EncryptedProviderConfig cuando
/// Provider = Microsoft365. [JsonPropertyName] explícito porque el JSON se produce
/// en camelCase (ver docs/06-...md §7 y el tool de prueba manual) y
/// JsonSerializer.Deserialize hace match de nombre case-SENSITIVE por defecto -- sin
/// esto, las 3 propiedades deserializan como null en silencio (bug real encontrado
/// probando contra un Workspace real: el equivalente de Google fallaba con "No
/// supported key formats were found" porque PrivateKeyPem llegaba null).
/// </summary>
internal sealed record Microsoft365ProviderConfig(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("clientSecret")] string ClientSecret);
