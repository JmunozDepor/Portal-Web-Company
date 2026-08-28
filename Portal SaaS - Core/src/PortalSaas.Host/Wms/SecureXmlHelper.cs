using System.Xml;
using System.Xml.Linq;

namespace PortalSaas.Host.Wms;

public static class SecureXmlHelper
{
    public static XDocument ParseSecurely(string xmlContent)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        using var stringReader = new StringReader(xmlContent);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(xmlReader);
    }

    public static string? GetValue(XElement? root, string parentLocalName, string childLocalName)
    {
        var parent = root?.Descendants().FirstOrDefault(x => x.Name.LocalName == parentLocalName);
        return parent?.Elements().FirstOrDefault(x => x.Name.LocalName == childLocalName)?.Value;
    }
}
