using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// AES-256-GCM con una clave maestra única (`Security:MasterSecretKey`, 32 bytes en
/// base64) resuelta desde configuración (dotnet user-secrets en desarrollo, vault en
/// producción -- nunca appsettings.json). Portado tal cual de PortalSAP_v2
/// (`SecretoCifradoService`), sin cambios de lógica, solo de nombres (inglés, ver
/// docs/01-CONVENCION-NOMBRES-BD.md). Cada valor cifrado lleva su propio nonce (12
/// bytes), así que un mismo texto plano nunca produce el mismo cifrado dos veces.
/// Formato persistido: base64(nonce || tag || ciphertext).
/// </summary>
public sealed class SecretoCifradoService : ISecretoCifradoService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _masterKey;

    public SecretoCifradoService(IConfiguration configuration)
    {
        var keyBase64 = configuration["Security:MasterSecretKey"]
            ?? throw new InvalidOperationException(
                "Falta configurar Security:MasterSecretKey (dotnet user-secrets/vault) -- clave AES-256 de 32 bytes en base64.");

        _masterKey = Convert.FromBase64String(keyBase64);
        if (_masterKey.Length != 32)
        {
            throw new InvalidOperationException("Security:MasterSecretKey debe ser una clave AES-256 de 32 bytes (base64).");
        }
    }

    public string Encrypt(string plainText)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var ciphertext = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aesGcm = new AesGcm(_masterKey, TagSize);
        aesGcm.Encrypt(nonce, plainBytes, ciphertext, tag);

        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        var data = Convert.FromBase64String(cipherText);
        if (data.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("Secreto cifrado inválido o corrupto.");
        }

        var nonce = data[..NonceSize];
        var tag = data[NonceSize..(NonceSize + TagSize)];
        var ciphertext = data[(NonceSize + TagSize)..];
        var plainBytes = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(_masterKey, TagSize);
        aesGcm.Decrypt(nonce, ciphertext, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
