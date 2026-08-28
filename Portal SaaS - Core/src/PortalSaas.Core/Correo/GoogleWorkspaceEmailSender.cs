using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Correo;

/// <summary>
/// Envía correo vía Gmail API, autenticado con una cuenta de servicio de Google Cloud
/// con delegación de dominio completo (impersona la casilla de envío del Workspace,
/// `settings.SenderEmail`) -- flujo estándar para enviar correo "de aplicación" sin
/// un usuario interactivo detrás. VERIFICADO contra un Workspace real (24 jul 2026,
/// ver docs/06-...md §7) -- entrega confirmada de punta a punta con
/// tools/PortalSaas.Tools.EmailSmokeTest.
/// </summary>
internal sealed class GoogleWorkspaceEmailSender
{
    private readonly HttpClient _httpClient;

    public GoogleWorkspaceEmailSender(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task SendAsync(EmailSettings settings, GoogleWorkspaceProviderConfig config, EmailMessage message, CancellationToken ct)
    {
        var accessToken = await AcquireImpersonatedTokenAsync(config, settings.SenderEmail, ct);

        var rawMessage = GmailMessageBuilder.BuildRawMimeMessageBase64Url(settings, message);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://gmail.googleapis.com/gmail/v1/users/{Uri.EscapeDataString(settings.SenderEmail)}/messages/send")
        {
            Content = JsonContent.Create(new { raw = rawMessage }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, ct);
        await response.EnsureSuccessWithBodyAsync(ct);
    }

    private async Task<string> AcquireImpersonatedTokenAsync(GoogleWorkspaceProviderConfig config, string senderEmail, CancellationToken ct)
    {
        var assertion = GoogleServiceAccountJwtBuilder.BuildSignedAssertion(
            config.ClientEmail, config.PrivateKeyPem, senderEmail, DateTimeOffset.UtcNow);

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = assertion,
        });

        using var response = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", tokenRequest, ct);
        await response.EnsureSuccessWithBodyAsync(ct);

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Respuesta de token de Google vacía o inválida.");

        return body.AccessToken;
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}

/// <summary>
/// Forma del JSON descifrado de EmailSettings.EncryptedProviderConfig cuando
/// Provider = GoogleWorkspace. [JsonPropertyName] explícito -- ver el mismo
/// comentario en Microsoft365ProviderConfig (bug real: sin esto, PrivateKeyPem
/// deserializaba como null y RSA.ImportFromPem fallaba con "No supported key
/// formats were found", detectado probando contra un Workspace real).
/// </summary>
internal sealed record GoogleWorkspaceProviderConfig(
    [property: JsonPropertyName("clientEmail")] string ClientEmail,
    [property: JsonPropertyName("privateKeyPem")] string PrivateKeyPem);
