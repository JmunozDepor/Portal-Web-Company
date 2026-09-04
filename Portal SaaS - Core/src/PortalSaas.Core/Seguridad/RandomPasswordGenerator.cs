using System.Security.Cryptography;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Genera contraseñas aleatorias que siempre cumplen PasswordPolicy. Usa
/// RandomNumberGenerator (no System.Random) -- mismo criterio criptográfico que
/// PasswordHasher y PasswordResetService. Excluye caracteres visualmente ambiguos
/// (0/O, 1/l/I) para que un admin pueda transcribirla o leerla en voz alta sin errores.
/// </summary>
public static class RandomPasswordGenerator
{
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*-_=+?";
    private const string AllChars = Uppercase + Lowercase + Digits + Symbols;

    public static string Generate(int length = 14)
    {
        var chars = new char[length];

        // Garantiza al menos un carácter de cada categoría exigida por PasswordPolicy.
        chars[0] = PickFrom(Uppercase);
        chars[1] = PickFrom(Lowercase);
        chars[2] = PickFrom(Digits);
        chars[3] = PickFrom(Symbols);

        for (var i = 4; i < length; i++)
        {
            chars[i] = PickFrom(AllChars);
        }

        Shuffle(chars);

        return new string(chars);
    }

    private static char PickFrom(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static void Shuffle(char[] chars)
    {
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
    }
}
