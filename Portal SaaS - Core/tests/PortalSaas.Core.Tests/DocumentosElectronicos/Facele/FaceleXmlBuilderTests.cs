using System.Text;
using System.Xml.Linq;
using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Core.DocumentosElectronicos;
using PortalSaas.Core.DocumentosElectronicos.Proveedores.Facele;

namespace PortalSaas.Core.Tests.DocumentosElectronicos.Facele;

public class FaceleXmlBuilderTests
{
    private static readonly DteIssuerData Depor = new(
        "76030680-0", "COMERCIAL DEPOR LIMITADA", "IMPORTACION, EXPORTACION, COMERCIALIZACION", 464100,
        "CAMINO SAN ESTEBAN 1361", "SAN BERNARDO", "SANTIAGO", null);

    private static readonly DteTransport Transporte = new(
        "PJFX11", "JJKL22", "74564934-3", new DteDriver("17786044-1", "JOSE PEREZ PEREZ"),
        "Lira 200", "Santiago", "Santiago", null,
        new DateOnly(2026, 10, 22), new TimeOnly(8, 30), new DateOnly(2026, 10, 22));

    /// <summary>Equivalente canónico de docs/facele/ejemplo-guia52-venta-res154.xml (aceptado por FACELE caoba).</summary>
    private static DteDocument GuiaVenta() => new(
        new DteIdDoc(52, new DateOnly(2026, 10, 22), TransferIndicator: 1, DispatchType: 2),
        null,
        new DteReceiver("78454830-9", "SOC. DE INVERSIONES Y FINANCIAMIENTO LTDA.", "Compra al mayor", "Lira 200", "Santiago", "Santiago"),
        Transporte,
        null,
        new[] { new DteLine(1, "11791C-003-M", "Poler n Core Full Zipper", null, 1, "UN", 41160, 41160) },
        new[] { new DteReference(1, "33", 1000412, new DateOnly(2026, 2, 24), null, "Traslado de mercaderia facturada (manifiesto 1)") },
        Array.Empty<DteExtraField>());

    private static XElement Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "DocumentosElectronicos", "Facele", "Fixtures", name);
        var doc = XDocument.Load(path);
        doc.Root!.Element("Documento")!.SetAttributeValue("ID", "T52");
        return Normalize(doc.Root!);
    }

    private static XElement Normalize(XElement e) =>
        new(e.Name, e.Attributes(), e.HasElements ? e.Elements().Select(Normalize) : (object)e.Value.Trim());

    private static XElement Parse(string xml) => Normalize(XDocument.Parse(xml).Root!);

    [Fact]
    public void Build_Guia52Venta_IgualAlEjemploProbado()
    {
        var built = Parse(FaceleXmlBuilder.Build(GuiaVenta(), Depor));
        var expected = Fixture("ejemplo-guia52-venta-res154.xml");
        Assert.True(XNode.DeepEquals(expected, built), $"Esperado:\n{expected}\n\nObtenido:\n{built}");
    }

    [Fact]
    public void Build_DeclaraIso88591()
    {
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>", FaceleXmlBuilder.Build(GuiaVenta(), Depor));
    }

    [Fact]
    public void Build_TrasladoInterno_TotalesCeroConTasaIva()
    {
        var doc = GuiaVenta() with
        {
            IdDoc = new DteIdDoc(52, new DateOnly(2026, 10, 22), 5, 3),
            Lines = new[] { new DteLine(1, "CT-1", "Zapatilla", null, 3, "UN", null, 0) },
        };
        var totales = Parse(FaceleXmlBuilder.Build(doc, Depor)).Descendants("Totales").Single();
        Assert.Equal("0", totales.Element("MntNeto")!.Value);
        Assert.Equal("19", totales.Element("TasaIVA")!.Value);
        Assert.Equal("0", totales.Element("IVA")!.Value);
        Assert.Equal("0", totales.Element("MntTotal")!.Value);
        var detalle = Parse(FaceleXmlBuilder.Build(doc, Depor)).Descendants("Detalle").Single();
        Assert.Null(detalle.Element("PrcItem"));
        Assert.Equal("0", detalle.Element("MontoItem")!.Value);
    }

    [Fact]
    public void Build_TransporteEnOrdenRes154()
    {
        var doc = GuiaVenta() with { Transport = Transporte with { Customs = new DteCustoms(12.5m, 3, "TSM") } };
        var t = Parse(FaceleXmlBuilder.Build(doc, Depor)).Descendants("Transporte").Single();
        Assert.Equal(
            new[] { "Patente", "PatenteCarro", "RUTTrans", "Chofer", "DirDest", "CmnaDest", "CiudadDest", "Aduana", "FchSalida", "HraSalida", "FchLlegada" },
            t.Elements().Select(x => x.Name.LocalName));
        Assert.Equal(new[] { "NombreTransp", "PesoNeto" }, t.Element("Aduana")!.Elements().Select(x => x.Name.LocalName));
        Assert.Equal("12.5", t.Element("Aduana")!.Element("PesoNeto")!.Value);
    }

    [Fact]
    public void Build_SinCarro_PatenteCarroSinDato()
    {
        var doc = GuiaVenta() with { Transport = Transporte with { TrailerPlate = null } };
        var t = Parse(FaceleXmlBuilder.Build(doc, Depor)).Descendants("Transporte").Single();
        Assert.Equal("SIN DATO", t.Element("PatenteCarro")!.Value);
    }

    [Fact]
    public void Build_OverrideOrigen_Manda()
    {
        var doc = GuiaVenta() with { Issuer = new DteIssuerOverride("Bodega Norte 55", "Quilicura", null) };
        var e = Parse(FaceleXmlBuilder.Build(doc, Depor)).Descendants("Emisor").Single();
        Assert.Equal("Bodega Norte 55", e.Element("DirOrigen")!.Value);
        Assert.Equal("Quilicura", e.Element("CmnaOrigen")!.Value);
        Assert.Equal("SANTIAGO", e.Element("CiudadOrigen")!.Value);
    }

    [Fact]
    public void Build_EscapaYNormalizaLatin1()
    {
        var doc = GuiaVenta() with { Receiver = GuiaVenta().Receiver with { Name = "Comercial Depor & Cía – Ñuñoa “Sur” <x>" } };
        var xml = FaceleXmlBuilder.Build(doc, Depor);
        var bytes = FaceleXmlBuilder.Encode(xml);
        var decoded = Encoding.Latin1.GetString(bytes);
        var name = XDocument.Parse(decoded).Descendants("RznSocRecep").Single().Value;
        Assert.Contains("& Cía", name);
        Assert.Contains("Ñuñoa", name);
        Assert.Contains("<x>", name);
        Assert.All(name, c => Assert.True(c <= 0xFF, $"Carácter fuera de Latin-1: U+{(int)c:X4}"));
    }

    [Fact]
    public void Build_TruncaLargosSii()
    {
        var doc = GuiaVenta() with
        {
            Lines = new[] { new DteLine(1, "A", new string('x', 120), null, 1, "UN", null, 0) },
            Receiver = GuiaVenta().Receiver with { Activity = new string('g', 60) },
        };
        var root = Parse(FaceleXmlBuilder.Build(doc, Depor));
        Assert.Equal(80, root.Descendants("NmbItem").Single().Value.Length);
        Assert.Equal(40, root.Descendants("GiroRecep").Single().Value.Length);
    }

    [Fact]
    public void Build_SinFolioNiTed()
    {
        var root = Parse(FaceleXmlBuilder.Build(GuiaVenta(), Depor));
        Assert.Equal("0", root.Descendants("Folio").Single().Value);
        Assert.Empty(root.Descendants("TED"));
        Assert.Empty(root.Descendants("TmstFirma"));
    }

    [Fact]
    public void Build_EmojiYSurrogatesNoRompen()
    {
        var doc = GuiaVenta() with
        {
            Receiver = GuiaVenta().Receiver with { Name = "Caja 📦 ‐ 𝔸 Ñandú" },
            Lines = new[] { new DteLine(1, "A", "Zapatilla 👟", null, 1, "UN", null, 0) },
        };
        var bytes = FaceleXmlBuilder.Encode(FaceleXmlBuilder.Build(doc, Depor));
        var root = XDocument.Parse(Encoding.Latin1.GetString(bytes));
        var name = root.Descendants("RznSocRecep").Single().Value;
        Assert.Contains("Caja", name);
        Assert.Contains("Ñandú", name);
        Assert.All(name, c => Assert.True(c <= 0xFF));
        Assert.StartsWith("Zapatilla", root.Descendants("NmbItem").Single().Value);
    }
}
