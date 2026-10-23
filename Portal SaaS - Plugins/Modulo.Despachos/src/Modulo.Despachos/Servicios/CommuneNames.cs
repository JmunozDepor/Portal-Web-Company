using System.Globalization;
using System.Text;

namespace Modulo.Despachos.Servicios;

/// <summary>
/// Normaliza nombres de comuna para compararlos: mayúsculas, sin tildes (Ñ = N), la puntuación cuenta como
/// espacio, espacios repetidos fuera y abreviaturas típicas expandidas (STGO, PTE, STA...). Además resuelve de
/// fábrica las variantes no oficiales más comunes ("SANTIAGO CENTRO" = Santiago).
/// </summary>
public static class CommuneNames
{
    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.Ordinal)
    {
        ["STGO"] = "SANTIAGO",
        ["PTE"] = "PUENTE",
        ["STA"] = "SANTA",
        ["STO"] = "SANTO",
        ["QTA"] = "QUINTA",
        ["GRAL"] = "GENERAL",
        ["EST"] = "ESTACION",
    };

    /// <summary>Variantes no oficiales (ya normalizadas) → nombre oficial normalizado.</summary>
    private static readonly Dictionary<string, string> BuiltInAliases = new(StringComparer.Ordinal)
    {
        ["SANTIAGO CENTRO"] = "SANTIAGO",
        ["SANTIAGO CENTRAL"] = "SANTIAGO",
        ["PAC"] = "PEDRO AGUIRRE CERDA",
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
        }
        var words = sb.ToString().Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => Abbreviations.TryGetValue(w, out var full) ? full : w);
        var text = string.Join(' ', words);
        return BuiltInAliases.TryGetValue(text, out var official) ? official : text;
    }
}
