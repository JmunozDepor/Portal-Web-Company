using System.Text.RegularExpressions;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Política de complejidad estándar (spec 2026-09-04): mínimo 10 caracteres, al menos
/// 1 mayúscula, 1 minúscula, 1 dígito, 1 símbolo. Se aplica tanto a la contraseña que
/// elige un usuario (ResetPassword.cshtml.cs) como a la que genera RandomPasswordGenerator.
/// </summary>
public static class PasswordPolicy
{
    private const int MinLength = 10;
    private static readonly Regex SymbolPattern = new(@"[^a-zA-Z0-9]", RegexOptions.Compiled);

    public static IReadOnlyList<string> Validate(string password)
    {
        var errores = new List<string>();

        if (password.Length < MinLength)
        {
            errores.Add($"La contraseña debe tener al menos {MinLength} caracteres.");
        }

        if (!password.Any(char.IsUpper))
        {
            errores.Add("La contraseña debe incluir al menos una mayúscula.");
        }

        if (!password.Any(char.IsLower))
        {
            errores.Add("La contraseña debe incluir al menos una minúscula.");
        }

        if (!password.Any(char.IsDigit))
        {
            errores.Add("La contraseña debe incluir al menos un número.");
        }

        if (!SymbolPattern.IsMatch(password))
        {
            errores.Add("La contraseña debe incluir al menos un símbolo.");
        }

        return errores;
    }
}
