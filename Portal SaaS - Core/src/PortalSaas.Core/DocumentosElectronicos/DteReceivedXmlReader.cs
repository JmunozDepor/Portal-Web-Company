using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using PortalSaas.Abstractions.Modelos.Dte;

namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>Lee el detalle (Documento/Detalle) de un DTE del SII archivado, sin importar el namespace.</summary>
internal static class DteReceivedXmlReader
{
    public static IReadOnlyList<DteReceivedLine> ReadLines(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return Array.Empty<DteReceivedLine>();
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return Array.Empty<DteReceivedLine>();
        }

        return doc.Descendants().Where(e => e.Name.LocalName == "Detalle")
            .Select(d => new DteReceivedLine(
                (int)(Dec(Child(d, "NroLinDet")) ?? 0),
                d.Elements().FirstOrDefault(e => e.Name.LocalName == "CdgItem") is { } code ? Child(code, "VlrCodigo") : null,
                Child(d, "NmbItem") ?? "",
                Dec(Child(d, "QtyItem")) ?? 0,
                Dec(Child(d, "PrcItem")),
                (long)(Dec(Child(d, "MontoItem")) ?? 0)))
            .ToList();
    }

    private static string? Child(XElement e, string name) =>
        e.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } v ? v : null;

    private static decimal? Dec(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
}
