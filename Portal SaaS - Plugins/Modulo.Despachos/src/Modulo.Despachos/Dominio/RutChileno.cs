using System.Text;

namespace Modulo.Despachos.Dominio;

/// <summary>Validación y normalización de RUT chileno (módulo 11). Forma canónica: "12345678-K".</summary>
public static class RutChileno
{
    public static bool IsValid(string? rut) => TryNormalize(rut, out _);

    /// <summary>Deja el RUT sin puntos, con guion y DV en mayúscula. Lanza si no tiene forma de RUT.</summary>
    public static string Normalize(string rut)
    {
        var s = Clean(rut);
        if (s.Length < 2) throw new ArgumentException("RUT inválido.", nameof(rut));
        return $"{s[..^1]}-{s[^1]}";
    }

    private static bool TryNormalize(string? raw, out string rut)
    {
        rut = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = Clean(raw);
        if (s.Length < 2 || s.Length > 9) return false;
        var cuerpo = s[..^1];
        if (!cuerpo.All(char.IsDigit) || !long.TryParse(cuerpo, out var n) || n <= 0) return false;
        if (CalcularDv(n) != s[^1]) return false;
        rut = $"{cuerpo}-{s[^1]}";
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

    private static char CalcularDv(long cuerpo)
    {
        var suma = 0;
        var factor = 2;
        foreach (var d in cuerpo.ToString().Reverse())
        {
            suma += (d - '0') * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }
        return (11 - suma % 11) switch { 11 => '0', 10 => 'K', var r => (char)('0' + r) };
    }
}
