using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PortalSaas.Core.Correo;

/// <summary>
/// Arma y firma (RS256) el JWT assertion que exige el flujo de cuenta de servicio de
/// Google con delegación de dominio completo ("domain-wide delegation") para
/// impersonar la casilla de envío -- separado en un método puro (sin HTTP) para poder
/// testearlo con un par de llaves RSA de prueba, sin credenciales reales de Google.
/// Referencia: https://developers.google.com/identity/protocols/oauth2/service-account
/// </summary>
internal static class GoogleServiceAccountJwtBuilder
{
    public const string GmailSendScope = "https://www.googleapis.com/auth/gmail.send";
    private const string TokenAudience = "https://oauth2.googleapis.com/token";

    /// <param name="clientEmail">`client_email` del JSON de la cuenta de servicio.</param>
    /// <param name="privateKeyPem">`private_key` del JSON de la cuenta de servicio (PEM completo).</param>
    /// <param name="impersonatedUserEmail">Casilla real a impersonar (`sub`) -- debe tener delegación de dominio autorizada en el Workspace.</param>
    public static string BuildSignedAssertion(string clientEmail, string privateKeyPem, string impersonatedUserEmail, DateTimeOffset now)
    {
        var header = new { alg = "RS256", typ = "JWT" };
        var claims = new
        {
            iss = clientEmail,
            sub = impersonatedUserEmail,
            scope = GmailSendScope,
            aud = TokenAudience,
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddMinutes(50).ToUnixTimeSeconds(),
        };

        var headerSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var claimsSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(claims));
        var unsignedToken = $"{headerSegment}.{claimsSegment}";

        using var rsa = RSA.Create();
        // Defensa adicional -- Pages/Admin/Organizations/EmailSettings/Index.cshtml.cs ya
        // normaliza esto al guardar, pero una config guardada ANTES de ese fix (con "\n"
        // literal en vez de saltos de línea reales, el formato tal cual trae el JSON de
        // cuenta de servicio de Google) sigue rota hasta que alguien la vuelva a pegar --
        // normalizar acá también para que funcione sin depender de un re-guardado manual.
        rsa.ImportFromPem(privateKeyPem.Replace("\\r\\n", "\n").Replace("\\n", "\n"));
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(unsignedToken), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{unsignedToken}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
