using System.Globalization;
using System.Text.RegularExpressions;

namespace Modulo.Despachos.Dominio;

/// <summary>
/// Código visible del borrador de guía manual: "GM-" + correlativo de 6 dígitos. Se escanea o digita en el manifiesto,
/// como la hoja AT de Agrupar traslados.
/// </summary>
public static class ManualGuideCode
{
    private static readonly Regex Shape = new(@"^GM-(\d{1,9})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Format(int number) => "GM-" + number.ToString("D6", CultureInfo.InvariantCulture);

    public static bool TryParse(string? raw, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var m = Shape.Match(raw.Trim());
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
