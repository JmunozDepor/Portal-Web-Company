using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Servicios.Common.Contratos;

namespace Servicios.Common.Seguridad;

/// <summary>
/// Copia deliberada, byte a byte del algoritmo, de
/// PortalSAP.Core.Seguridad.SecretoCifradoService (PortalSAP_v2/Proyecto Saas Portal) --
/// ver docs/00-VINCULO-CON-PORTAL-SAAS.md para por qué es copia y no dependencia cruzada
/// de proyecto. AES-256-GCM con una clave maestra única (Seguridad:ClaveMaestraSecretos,
/// 32 bytes en base64) resuelta desde configuración (dotnet user-secrets en desarrollo,
/// vault en producción -- nunca appsettings.json). Cada valor cifrado lleva su propio
/// nonce (12 bytes) al principio, así que un mismo texto plano nunca produce el mismo
/// cifrado dos veces. Formato persistido: base64(nonce || tag || ciphertext).
/// </summary>
public sealed class SecretoCifradoService : ISecretoCifradoService
{
    private const int TamanoNonce = 12;
    private const int TamanoTag = 16;

    private readonly byte[] _claveMaestra;

    public SecretoCifradoService(IConfiguration configuracion)
    {
        var claveBase64 = configuracion["Seguridad:ClaveMaestraSecretos"]
            ?? throw new InvalidOperationException(
                "Falta configurar Seguridad:ClaveMaestraSecretos (dotnet user-secrets/vault) -- clave AES-256 de 32 bytes en base64.");

        _claveMaestra = Convert.FromBase64String(claveBase64);
        if (_claveMaestra.Length != 32)
        {
            throw new InvalidOperationException("Seguridad:ClaveMaestraSecretos debe ser una clave AES-256 de 32 bytes (base64).");
        }
    }

    public string Cifrar(string textoPlano)
    {
        var nonce = RandomNumberGenerator.GetBytes(TamanoNonce);
        var textoPlanoBytes = Encoding.UTF8.GetBytes(textoPlano);
        var ciphertext = new byte[textoPlanoBytes.Length];
        var tag = new byte[TamanoTag];

        using var aesGcm = new AesGcm(_claveMaestra, TamanoTag);
        aesGcm.Encrypt(nonce, textoPlanoBytes, ciphertext, tag);

        var resultado = new byte[TamanoNonce + TamanoTag + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, resultado, 0, TamanoNonce);
        Buffer.BlockCopy(tag, 0, resultado, TamanoNonce, TamanoTag);
        Buffer.BlockCopy(ciphertext, 0, resultado, TamanoNonce + TamanoTag, ciphertext.Length);

        return Convert.ToBase64String(resultado);
    }

    public string Descifrar(string textoCifrado)
    {
        var datos = Convert.FromBase64String(textoCifrado);
        if (datos.Length < TamanoNonce + TamanoTag)
        {
            throw new InvalidOperationException("Secreto cifrado inválido o corrupto.");
        }

        var nonce = datos[..TamanoNonce];
        var tag = datos[TamanoNonce..(TamanoNonce + TamanoTag)];
        var ciphertext = datos[(TamanoNonce + TamanoTag)..];
        var textoPlanoBytes = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(_claveMaestra, TamanoTag);
        aesGcm.Decrypt(nonce, ciphertext, tag, textoPlanoBytes);

        return Encoding.UTF8.GetString(textoPlanoBytes);
    }
}
