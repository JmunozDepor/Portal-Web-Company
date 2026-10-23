using System.Text;

namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>
/// Validación y normalización de RUT chileno (módulo 11). Forma canónica "12345678-K".
/// Copia deliberada de Modulo.Despachos.Dominio.RutChileno (los plugins no comparten código con Core).
/// </summary>
public static class Rut
{
    public static bool IsValid(string? rut) => TryNormalize(rut, out _);

    public static bool TryNormalize(string? raw, out string rut)
    {
        rut = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = Clean(raw);
        if (s.Length < 2 || s.Length > 9) return false;
        var body = s[..^1];
        if (!body.All(char.IsDigit) || !long.TryParse(body, out var n) || n <= 0) return false;
        if (CheckDigit(n) != s[^1]) return false;
        rut = $"{body}-{s[^1]}";
        return true;
    }

    private static string Clean(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsDigit(c)) sb.Append(c);
            else if (c is 'k' or 'K') sb.Append('K');
        }
        return sb.ToString();
    }

    private static char CheckDigit(long body)
    {
        var sum = 0;
        var factor = 2;
        foreach (var d in body.ToString().Reverse())
        {
            sum += (d - '0') * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }
        return (11 - sum % 11) switch { 11 => '0', 10 => 'K', var r => (char)('0' + r) };
    }
}
