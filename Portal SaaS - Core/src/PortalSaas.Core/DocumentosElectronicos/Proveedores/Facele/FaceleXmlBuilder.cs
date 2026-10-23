using System.Globalization;
using System.Text;
using System.Xml;
using PortalSaas.Abstractions.Modelos.Dte;

namespace PortalSaas.Core.DocumentosElectronicos.Proveedores.Facele;

/// <summary>
/// Traduce el modelo canónico al XML del DTE del SII (formato 2.5) que espera DoceleOL.generaDTE:
/// &lt;DTE version="1.0"&gt;&lt;Documento&gt; sin folio (0), sin TED, TmstFirma ni firma (FACELE los agrega),
/// codificado ISO-8859-1. Orden de tags = orden del XSD del SII (validado en caoba, ver
/// Modulo.Despachos/docs/facele/*.xml). Los campos libres (Extras) no se envían todavía: FACELE
/// exige el formato XML_OBS y habilitarlos en su plantilla.
/// </summary>
public static class FaceleXmlBuilder
{
    private const string NoData = "SIN DATO";
    private const decimal DefaultVatRate = 19m;

    public static string Build(DteDocument doc, DteIssuerData issuer)
    {
        var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false, Encoding = Encoding.Latin1 };
        using var sw = new Latin1StringWriter();
        using (var w = XmlWriter.Create(sw, settings))
        {
            w.WriteStartDocument();
            w.WriteStartElement("DTE");
            w.WriteAttributeString("version", "1.0");
            w.WriteStartElement("Documento");
            w.WriteAttributeString("ID", $"T{doc.IdDoc.DteType}");

            w.WriteStartElement("Encabezado");
            WriteIdDoc(w, doc.IdDoc);
            WriteIssuer(w, issuer, doc.Issuer);
            WriteReceiver(w, doc.Receiver);
            if (doc.Transport is { } t) WriteTransport(w, t);
            WriteTotals(w, doc.Totals ?? ComputeTotals(doc.Lines));
            w.WriteEndElement();

            foreach (var l in doc.Lines) WriteLine(w, l);
            foreach (var r in doc.References) WriteReference(w, r);

            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndDocument();
        }
        // XmlWriter declara "iso-8859-1" en minúsculas; se deja igual al ejemplo probado con FACELE.
        var xml = sw.ToString();
        var end = xml.IndexOf("?>", StringComparison.Ordinal);
        return "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>" + xml[(end + 2)..];
    }

    /// <summary>Bytes ISO-8859-1 del XML (lo que se envía y lo que FACELE guarda).</summary>
    public static byte[] Encode(string xml) => Encoding.Latin1.GetBytes(xml);

    private static void WriteIdDoc(XmlWriter w, DteIdDoc id)
    {
        w.WriteStartElement("IdDoc");
        Elem(w, "TipoDTE", id.DteType.ToString(CultureInfo.InvariantCulture));
        Elem(w, "Folio", "0");
        Elem(w, "FchEmis", Date(id.IssueDate));
        if (id.DispatchType is { } dt) Elem(w, "TipoDespacho", dt.ToString(CultureInfo.InvariantCulture));
        if (id.TransferIndicator is { } ti) Elem(w, "IndTraslado", ti.ToString(CultureInfo.InvariantCulture));
        w.WriteEndElement();
    }

    private static void WriteIssuer(XmlWriter w, DteIssuerData issuer, DteIssuerOverride? o)
    {
        w.WriteStartElement("Emisor");
        Elem(w, "RUTEmisor", issuer.TaxId);
        Elem(w, "RznSoc", issuer.Name, 100);
        Elem(w, "GiroEmis", issuer.Activity, 80);
        Elem(w, "Acteco", issuer.ActivityCode.ToString(CultureInfo.InvariantCulture));
        if (issuer.SiiBranchCode is { } b) Elem(w, "CdgSIISucur", b.ToString(CultureInfo.InvariantCulture));
        Elem(w, "DirOrigen", Pick(o?.OriginAddress, issuer.Address), 70);
        Elem(w, "CmnaOrigen", Pick(o?.OriginCommune, issuer.Commune), 20);
        Elem(w, "CiudadOrigen", Pick(o?.OriginCity, issuer.City), 20);
        w.WriteEndElement();
    }

    private static void WriteReceiver(XmlWriter w, DteReceiver r)
    {
        w.WriteStartElement("Receptor");
        Elem(w, "RUTRecep", r.TaxId);
        Elem(w, "RznSocRecep", r.Name, 100);
        OptElem(w, "GiroRecep", r.Activity, 40);
        OptElem(w, "DirRecep", r.Address, 70);
        OptElem(w, "CmnaRecep", r.Commune, 20);
        OptElem(w, "CiudadRecep", r.City, 20);
        w.WriteEndElement();
    }

    private static void WriteTransport(XmlWriter w, DteTransport t)
    {
        w.WriteStartElement("Transporte");
        OptElem(w, "Patente", t.Plate, 8);
        Elem(w, "PatenteCarro", string.IsNullOrWhiteSpace(t.TrailerPlate) ? NoData : t.TrailerPlate, 8);
        OptElem(w, "RUTTrans", t.CarrierTaxId);
        if (t.Driver is { } d)
        {
            w.WriteStartElement("Chofer");
            Elem(w, "RUTChofer", d.TaxId);
            Elem(w, "NombreChofer", d.Name, 30);
            w.WriteEndElement();
        }
        OptElem(w, "DirDest", t.DestinationAddress, 70);
        OptElem(w, "CmnaDest", t.DestinationCommune, 20);
        OptElem(w, "CiudadDest", t.DestinationCity, 20);
        if (t.Customs is { } c && (c.NetWeightKg is not null || !string.IsNullOrWhiteSpace(c.CarrierName)))
        {
            // Solo lo probado en caoba (NombreTransp + PesoNeto); TotBultos queda pendiente de validar con FACELE.
            w.WriteStartElement("Aduana");
            OptElem(w, "NombreTransp", c.CarrierName, 40);
            if (c.NetWeightKg is { } kg) Elem(w, "PesoNeto", Num(kg));
            w.WriteEndElement();
        }
        if (t.DepartureDate is { } fs) Elem(w, "FchSalida", Date(fs));
        if (t.DepartureTime is { } hs) Elem(w, "HraSalida", hs.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        if (t.ArrivalDate is { } fl) Elem(w, "FchLlegada", Date(fl));
        w.WriteEndElement();
    }

    private static void WriteTotals(XmlWriter w, DteTotals t)
    {
        w.WriteStartElement("Totales");
        Elem(w, "MntNeto", Num(t.Net));
        if (t.Exempt > 0) Elem(w, "MntExe", Num(t.Exempt));
        Elem(w, "TasaIVA", Num(t.VatRate));
        Elem(w, "IVA", Num(t.Vat));
        Elem(w, "MntTotal", Num(t.Total));
        w.WriteEndElement();
    }

    private static void WriteLine(XmlWriter w, DteLine l)
    {
        w.WriteStartElement("Detalle");
        Elem(w, "NroLinDet", l.LineNumber.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(l.ItemCode))
        {
            w.WriteStartElement("CdgItem");
            Elem(w, "TpoCodigo", "INT1");
            Elem(w, "VlrCodigo", l.ItemCode, 35);
            w.WriteEndElement();
        }
        Elem(w, "NmbItem", l.Name, 80);
        OptElem(w, "DscItem", l.Description, 1000);
        Elem(w, "QtyItem", Num(l.Quantity));
        OptElem(w, "UnmdItem", l.Unit, 4);
        if (l.UnitPrice is { } p) Elem(w, "PrcItem", Num(p));
        Elem(w, "MontoItem", Num(Math.Round(l.Amount, 0, MidpointRounding.AwayFromZero)));
        w.WriteEndElement();
    }

    private static void WriteReference(XmlWriter w, DteReference r)
    {
        w.WriteStartElement("Referencia");
        Elem(w, "NroLinRef", r.LineNumber.ToString(CultureInfo.InvariantCulture));
        Elem(w, "TpoDocRef", r.DocumentType, 3);
        if (r.Folio is { } f) Elem(w, "FolioRef", f.ToString(CultureInfo.InvariantCulture));
        Elem(w, "FchRef", Date(r.Date));
        if (r.ReasonCode is { } c) Elem(w, "CodRef", c.ToString(CultureInfo.InvariantCulture));
        OptElem(w, "RazonRef", r.Reason, 90);
        w.WriteEndElement();
    }

    /// <summary>Totales desde las líneas: neto = Σ montos, IVA 19% redondeado a pesos (todo 0 si las líneas valen 0).</summary>
    internal static DteTotals ComputeTotals(IReadOnlyList<DteLine> lines)
    {
        var net = Math.Round(lines.Sum(l => l.Amount), 0, MidpointRounding.AwayFromZero);
        var vat = Math.Round(net * DefaultVatRate / 100m, 0, MidpointRounding.AwayFromZero);
        return new DteTotals(net, 0, DefaultVatRate, vat, net + vat);
    }

    private static void Elem(XmlWriter w, string name, string value, int? max = null) =>
        w.WriteElementString(name, Clean(value, max));

    private static void OptElem(XmlWriter w, string name, string? value, int? max = null)
    {
        if (!string.IsNullOrWhiteSpace(value)) Elem(w, name, value, max);
    }

    private static string Pick(string? preferred, string fallback) => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Num(decimal v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>Trim, sin caracteres de control, solo Latin-1 (equivalentes para comillas/guiones tipográficos) y truncado.</summary>
    internal static string Clean(string value, int? max)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (char.IsControl(ch)) { sb.Append(' '); continue; }
            if (ch <= 0xFF) { sb.Append(ch); continue; }
            var mapped = ch switch
            {
                '–' or '—' or '−' => "-",
                '“' or '”' or '„' => "\"",
                '‘' or '’' or '‚' => "'",
                '…' => "...",
                _ => StripAccents(ch),
            };
            sb.Append(mapped);
        }
        var s = sb.ToString();
        return max is { } m && s.Length > m ? s[..m].TrimEnd() : s;
    }

    private static string StripAccents(char ch)
    {
        var basic = ch.ToString().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c <= 0xFF)
            .ToArray();
        return basic.Length > 0 ? new string(basic) : "?";
    }

    private sealed class Latin1StringWriter : StringWriter
    {
        public Latin1StringWriter() : base(CultureInfo.InvariantCulture) { }
        public override Encoding Encoding => Encoding.Latin1;
    }
}
