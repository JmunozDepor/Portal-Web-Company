using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Comercial.Licenciamiento;

/// <summary>
/// ECDSA P-256 sobre un payload JSON canónico. Formato del token:
/// base64(payloadJson) + "." + base64(firma). Ver ILicenseTokenService.
///
/// Licensing:SigningPrivateKey (PKCS8, base64) solo existe en el central -- si no está
/// configurada, Sign() lanza (esta instalación no puede emitir licencias, solo
/// verificarlas, que es justamente el caso de una instalación on-premise).
/// Licensing:CentralPublicKey (SubjectPublicKeyInfo/X509, base64) no es secreta, va en
/// appsettings.json normal, y solo hace falta en instalaciones on-premise que
/// verifican licencia -- ambas claves se leen de forma perezosa (no en el
/// constructor), porque este servicio se inyecta también en organizaciones SaaS que
/// nunca configuran Licensing:*.
/// </summary>
public sealed class LicenseTokenService : ILicenseTokenService
{
    private readonly IConfiguration _configuration;

    public LicenseTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Sign(LicenseStatusPayload payload)
    {
        // Validación perezosa (no en el constructor): este servicio se inyecta en
        // OrganizationAccessGateService, que corre para TODAS las organizaciones
        // (saas y on_premise). Si el constructor exigiera estas claves, una
        // instalación SaaS pura -- que nunca configura Licensing:* -- rompería el
        // acceso de todo el mundo por un feature que ni siquiera usa.
        var privateKeyBase64 = _configuration["Licensing:SigningPrivateKey"]
            ?? throw new InvalidOperationException(
                "Falta configurar Licensing:SigningPrivateKey -- esta instalación no tiene la clave privada del servidor central y no puede firmar licencias.");
        var privateKeyPkcs8 = Convert.FromBase64String(privateKeyBase64);

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
        var signature = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256);

        return $"{Convert.ToBase64String(payloadBytes)}.{Convert.ToBase64String(signature)}";
    }

    public bool TryVerify(string token, out LicenseStatusPayload? payload)
    {
        payload = null;

        // Igual criterio perezoso que Sign(): si Licensing:CentralPublicKey no está
        // configurada, esta instalación simplemente no puede verificar licencias --
        // no es un error de arranque para quien no usa la feature.
        var publicKeyBase64 = _configuration["Licensing:CentralPublicKey"];
        if (string.IsNullOrWhiteSpace(publicKeyBase64))
        {
            return false;
        }

        var parts = token.Split('.', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        byte[] payloadBytes;
        byte[] signature;
        byte[] publicKeySpki;
        try
        {
            payloadBytes = Convert.FromBase64String(parts[0]);
            signature = Convert.FromBase64String(parts[1]);
            publicKeySpki = Convert.FromBase64String(publicKeyBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKeySpki, out _);
        if (!ecdsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256))
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<LicenseStatusPayload>(payloadBytes);
            return payload is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
