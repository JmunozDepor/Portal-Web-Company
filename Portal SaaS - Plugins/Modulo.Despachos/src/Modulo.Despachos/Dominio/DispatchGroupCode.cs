using System.Globalization;
using System.Text.RegularExpressions;

namespace Modulo.Despachos.Dominio;

/// <summary>
/// Código visible de una pre-guía: "PG-" + correlativo de 6 dígitos. Es el mismo texto del Code128 de la hoja que
/// despacho escanea en el manifiesto.
/// </summary>
public static class DispatchGroupCode
{
    private static readonly Regex Shape = new(@"^PG-(\d{1,9})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Format(int number) => "PG-" + number.ToString("D6", CultureInfo.InvariantCulture);

    public static bool TryParse(string? raw, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var m = Shape.Match(raw.Trim());
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
