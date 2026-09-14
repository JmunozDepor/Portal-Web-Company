using System.Security.Cryptography;

namespace Modulo.AuditoriaInventario.Servicios;

/// <summary>
/// PBKDF2-SHA256 con sal aleatoria por usuario -- mismo algoritmo que
/// PortalSaas.Core.Seguridad.PasswordHasher (100k iteraciones, sal de 128 bits,
/// hash de 256 bits), portado acá porque un plugin nunca referencia
/// PortalSaas.Core (solo PortalSaas.Abstractions, ver Modulo.AuditoriaInventario.csproj).
/// Reemplaza el placeholder SHA-256 sin sal que tenía AuditoriaInventarioApiService.
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
