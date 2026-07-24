using System.Security.Cryptography;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// PBKDF2-SHA256 para users.password_hash/password_salt. Salt aleatorio de 128 bits
/// por usuario, 100k iteraciones -- portado tal cual de PortalSAP_v2
/// (`PasswordHasher`), valores razonables para 2026, ajustar si OWASP actualiza la
/// recomendación.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;

    public static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string expectedHash, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(expectedHash));
    }
}
