using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Modulo.Despachos.Dominio;

public record TedData(string IssuerRut, int DocumentType, long Folio, DateOnly IssueDate, string ReceiverRut, decimal Amount);

/// <summary>
/// Parser del timbre electrónico (TED) del PDF417 de un DTE. Tolera ruido del lector
/// (CR/LF, espacios, texto alrededor) y no resuelve DTD ni entidades externas.
/// </summary>
public static class TedParser
{
    public static bool TryParse(string? raw, out TedData data)
    {
        data = default!;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var start = raw.IndexOf("<TED", StringComparison.Ordinal);
        var endTag = raw.LastIndexOf("</TED>", StringComparison.Ordinal);
        if (start < 0 || endTag < start) return false;
        var xml = raw[start..(endTag + "</TED>".Length)];

        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var sr = new StringReader(xml);
            using var reader = XmlReader.Create(sr, settings);
            var dd = XDocument.Load(reader).Root?.Element("DD");
            if (dd is null) return false;

            string? V(string n) => dd.Element(n)?.Value.Trim();
            var re = V("RE"); var rr = V("RR");
            if (string.IsNullOrEmpty(re) || string.IsNullOrEmpty(rr)) return false;
            if (!int.TryParse(V("TD"), NumberStyles.None, CultureInfo.InvariantCulture, out var td)) return false;
            if (!long.TryParse(V("F"), NumberStyles.None, CultureInfo.InvariantCulture, out var folio)) return false;
            if (!DateOnly.TryParseExact(V("FE"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fe)) return false;
            if (!decimal.TryParse(V("MNT"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var mnt)) return false;

            data = new TedData(re, td, folio, fe, rr, mnt);
            return true;
        }
        catch (XmlException) { return false; }
    }
}
