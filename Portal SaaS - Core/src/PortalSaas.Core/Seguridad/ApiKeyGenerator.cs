using System.Security.Cryptography;

namespace PortalSaas.Core.Seguridad;

public static class ApiKeyGenerator
{
    public static string GenerateRawKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string rawKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(rawKey);
        return Convert.ToBase64String(SHA256.HashData(bytes));
    }
}
