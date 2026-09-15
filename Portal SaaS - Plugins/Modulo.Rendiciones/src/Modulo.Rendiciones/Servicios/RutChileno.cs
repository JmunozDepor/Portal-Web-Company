using System.Text;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Normalización y validación de RUT chileno (dígito verificador, módulo 11). El OCR
/// devuelve el RUT con formato y errores variables ("76.543.210-K", "765432 10 k",
/// "76.543.210K"); acá se lleva a la forma canónica "76543210-K" y se descarta lo que
/// no valide, para no guardar basura en supplier_tax_id.
/// </summary>
public static class RutChileno
{
    /// <summary>
    /// Deja el RUT como "&lt;cuerpo&gt;-&lt;DV&gt;" (sin puntos, DV en mayúscula) SOLO si el
    /// dígito verificador es correcto. Si no parsea o no valida, devuelve null.
    /// </summary>
    public static string? NormalizeOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var limpio = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsDigit(c)) limpio.Append(c);
            else if (c is 'k' or 'K') limpio.Append('K');
        }

        var s = limpio.ToString();
        if (s.Length < 2 || s.Length > 9)
            return null;

        var cuerpo = s[..^1];
        var dv = s[^1];
        if (!cuerpo.All(char.IsDigit) || !long.TryParse(cuerpo, out var numero) || numero <= 0)
            return null;

        return CalcularDv(numero) == dv ? $"{cuerpo}-{dv}" : null;
    }

    /// <summary>DV esperado para un cuerpo de RUT (módulo 11, serie 2..7). Devuelve '0'..'9' o 'K'.</summary>
    public static char CalcularDv(long cuerpo)
    {
        var suma = 0;
        var factor = 2;
        foreach (var d in cuerpo.ToString().Reverse())
        {
            suma += (d - '0') * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }

        var resto = 11 - (suma % 11);
        return resto switch
        {
            11 => '0',
            10 => 'K',
            _ => (char)('0' + resto),
        };
    }
}
