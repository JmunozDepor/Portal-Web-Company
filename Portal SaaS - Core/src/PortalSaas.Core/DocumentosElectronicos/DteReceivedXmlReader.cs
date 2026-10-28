using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using PortalSaas.Abstractions.Modelos.Dte;

namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>Lee un DTE del SII archivado (Documento: Encabezado, Detalle, Referencia), sin importar el namespace.</summary>
internal static class DteReceivedXmlReader
{
    public static IReadOnlyList<DteReceivedLine> ReadLines(string? xml) =>
        Documento(xml) is { } documento ? Lines(documento) : Array.Empty<DteReceivedLine>();

    /// <summary>El documento completo; null si no hay XML o no trae Documento/Encabezado.</summary>
    public static DteReceivedDocument? ReadDocument(string? xml)
    {
        if (Documento(xml) is not { } documento || Element(documento, "Encabezado") is not { } header) return null;
        var id = Element(header, "IdDoc");
        var issuer = Element(header, "Emisor");
        var receiver = Element(header, "Receptor");
        var totals = Element(header, "Totales");

        return new DteReceivedDocument(
            DteType: Int(Child(id, "TipoDTE")) ?? 0,
            Folio: (long)(Dec(Child(id, "Folio")) ?? 0),
            IssueDate: Date(Child(id, "FchEmis")),
            DueDate: Date(Child(id, "FchVenc")),
            PaymentForm: Int(Child(id, "FmaPago")),
            PaymentTerms: Child(id, "TermPagoGlosa"),
            DispatchType: Int(Child(id, "TipoDespacho")),
            TransferIndicator: Int(Child(id, "IndTraslado")),
            ServiceIndicator: Int(Child(id, "IndServicio")),
            GrossAmounts: Child(id, "MntBruto") == "1",
            Issuer: new DteParty(
                Child(issuer, "RUTEmisor") ?? "", Child(issuer, "RznSoc") ?? Child(issuer, "RznSocEmisor") ?? "",
                Child(issuer, "GiroEmis") ?? Child(issuer, "GiroEmisor"), Child(issuer, "DirOrigen"), Child(issuer, "CmnaOrigen"),
                Child(issuer, "CiudadOrigen"), Phone: Child(issuer, "Telefono"), Email: Child(issuer, "CorreoEmisor")),
            Receiver: new DteParty(
                Child(receiver, "RUTRecep") ?? "", Child(receiver, "RznSocRecep") ?? "", Child(receiver, "GiroRecep"),
                Child(receiver, "DirRecep"), Child(receiver, "CmnaRecep"), Child(receiver, "CiudadRecep"),
                Email: Child(receiver, "CorreoRecep"), Contact: Child(receiver, "Contacto"),
                PostalAddress: Join(Child(receiver, "DirPostal"), Child(receiver, "CmnaPostal"), Child(receiver, "CiudadPostal"))),
            Transport: Transport(Element(header, "Transporte")),
            Lines: Lines(documento),
            References: documento.Elements().Where(e => e.Name.LocalName == "Referencia")
                .Select(r => new DteReceivedReference(Child(r, "TpoDocRef") ?? "", Child(r, "FolioRef") ?? "", Date(Child(r, "FchRef")),
                    Int(Child(r, "CodRef")), Child(r, "RazonRef")))
                .ToList(),
            Net: Amount(totals, "MntNeto"),
            Exempt: Amount(totals, "MntExe"),
            VatRate: Dec(Child(totals, "TasaIVA")),
            Vat: Amount(totals, "IVA"),
            OtherTaxes: totals?.Elements().Where(e => e.Name.LocalName == "ImptoReten")
                .Select(t => new DteOtherTax(Int(Child(t, "TipoImp")) ?? 0, Dec(Child(t, "TasaImp")), Amount(t, "MontoImp")))
                .ToList() ?? new List<DteOtherTax>(),
            Total: Amount(totals, "MntTotal"));
    }

    private static XElement? Documento(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        try
        {
            return XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "Documento");
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static IReadOnlyList<DteReceivedLine> Lines(XElement documento) =>
        documento.Elements().Where(e => e.Name.LocalName == "Detalle")
            .Select(d => new DteReceivedLine(
                Int(Child(d, "NroLinDet")) ?? 0,
                Element(d, "CdgItem") is { } code ? Child(code, "VlrCodigo") : null,
                Child(d, "NmbItem") ?? "",
                Dec(Child(d, "QtyItem")) ?? 0,
                Dec(Child(d, "PrcItem")),
                Amount(d, "MontoItem"),
                Child(d, "DscItem"),
                Child(d, "UnmdItem"),
                Dec(Child(d, "DescuentoPct")),
                Dec(Child(d, "DescuentoMonto")) is { } discount ? (long)discount : null))
            .ToList();

    private static DteReceivedTransport? Transport(XElement? t)
    {
        if (t is null) return null;
        var driver = Element(t, "Chofer");
        return new DteReceivedTransport(Child(t, "Patente"), Child(t, "RUTTrans"), Child(driver, "RUTChofer"), Child(driver, "NombreChofer"),
            Child(t, "DirDest"), Child(t, "CmnaDest"), Child(t, "CiudadDest"));
    }

    private static XElement? Element(XElement? e, string name) => e?.Elements().FirstOrDefault(x => x.Name.LocalName == name);

    private static string? Child(XElement? e, string name) =>
        Element(e, name)?.Value.Trim() is { Length: > 0 } v ? v : null;

    private static string? Join(params string?[] parts) =>
        parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() is { Count: > 0 } list ? string.Join(", ", list) : null;

    private static long Amount(XElement? e, string name) => (long)(Dec(Child(e, name)) ?? 0);

    private static int? Int(string? value) => Dec(value) is { } d ? (int)d : null;

    private static decimal? Dec(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
