using System.Globalization;
using System.Text.RegularExpressions;

namespace Modulo.Despachos.Dominio;

/// <summary>
/// Código visible de una agrupación: "AT-" + correlativo de 6 dígitos. Es el mismo texto del Code128 de la hoja que
/// despacho escanea en el manifiesto.
/// </summary>
public static class DispatchGroupCode
{
    private static readonly Regex Shape = new(@"^(AT|EG)-(\d{1,9})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>"AT-" para traslados, "EG-" para Entregas con guía previa (mismo correlativo por compañía).</summary>
    public static string Format(int number, string kind = Models.DispatchGroupKinds.Transfer) =>
        (kind == Models.DispatchGroupKinds.Delivery ? "EG-" : "AT-") + number.ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>Solo hojas de traslados (AT-).</summary>
    public static bool TryParse(string? raw, out int number) =>
        TryParse(raw, out number, out var kind) && kind == Models.DispatchGroupKinds.Transfer;

    /// <summary>Hoja AT- o EG-, con su tipo.</summary>
    public static bool TryParse(string? raw, out int number, out string kind)
    {
        number = 0;
        kind = Models.DispatchGroupKinds.Transfer;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var m = Shape.Match(raw.Trim());
        if (!m.Success || !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number)) return false;
        if (m.Groups[1].Value.Equals("EG", StringComparison.OrdinalIgnoreCase)) kind = Models.DispatchGroupKinds.Delivery;
        return true;
    }
}
