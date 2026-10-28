using PortalSaas.Core.DocumentosElectronicos;

namespace PortalSaas.Core.Tests.DocumentosElectronicos;

public class DteReceivedXmlReaderTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DocumentosElectronicos", "Fixtures", name));

    /// <summary>XML con el namespace del SII y declaración ISO-8859-1 (como lo archiva el SII).</summary>
    [Fact]
    public void ReadDocument_FacturaConNamespace_EncabezadoTotalesLineasYReferencia()
    {
        var d = DteReceivedXmlReader.ReadDocument(Fixture("factura33-recibida.xml"))!;

        Assert.Equal(33, d.DteType);
        Assert.Equal(18452, d.Folio);
        Assert.Equal(new DateOnly(2026, 10, 23), d.IssueDate);
        Assert.Equal("76412553-1", d.Issuer.TaxId);
        Assert.Equal("Comercial Textil Andes SpA", d.Issuer.Name);
        Assert.Equal("Comercial Depor Ltda.", d.Receiver.Name);
        Assert.Equal(2000000, d.Net);
        Assert.Equal(19m, d.VatRate);
        Assert.Equal(380000, d.Vat);
        Assert.Equal(2380000, d.Total);
        Assert.Null(d.Transport);
        Assert.Equal(2, d.Lines.Count);
        Assert.Equal("POL-001", d.Lines[0].ItemCode);
        var reference = Assert.Single(d.References);
        Assert.Equal(("801", "4512", new DateOnly(2026, 10, 12)), (reference.Type, reference.Folio, reference.Date));
    }

    /// <summary>Estructura real de alerce (sin namespace): emisor y receptor completos, transporte, descripción y descuentos.</summary>
    [Fact]
    public void ReadDocument_ExentaConTransporteYDescripciones()
    {
        var d = DteReceivedXmlReader.ReadDocument(Fixture("exenta34-recibida.xml"))!;

        Assert.Equal(34, d.DteType);
        Assert.Equal(2, d.PaymentForm);
        Assert.Equal("45 DIAS", d.PaymentTerms);
        Assert.Equal(new DateOnly(2026, 12, 12), d.DueDate);

        Assert.Equal("AGENCIA DE CARGA Y SERVICIOS LOGISTICOS", d.Issuer.Activity);
        Assert.Equal("+56 2 2345 6789", d.Issuer.Phone);
        Assert.Equal("facturas@logisur.example", d.Issuer.Email);
        Assert.Equal(("AV. PROVIDENCIA 1000 OF 201", "PROVIDENCIA", "SANTIAGO"), (d.Issuer.Address, d.Issuer.Commune, d.Issuer.City));

        Assert.Equal("76030680-0", d.Receiver.TaxId);
        Assert.Equal("VENTA AL POR MAYOR DE PRODUCTOS TEXTILES", d.Receiver.Activity);
        Assert.Equal("CONTACTO PRUEBA", d.Receiver.Contact);
        Assert.Equal("dte@depor.example", d.Receiver.Email);
        Assert.Equal("CASILLA 123, LAS CONDES, SANTIAGO", d.Receiver.PostalAddress);

        Assert.NotNull(d.Transport);
        Assert.Equal(("AB1234", "77888999-0", "JUAN PRUEBA"), (d.Transport!.Plate, d.Transport.CarrierTaxId, d.Transport.DriverName));
        Assert.Equal(("BODEGA CENTRAL", "PUDAHUEL", "SANTIAGO"), (d.Transport.Address, d.Transport.Commune, d.Transport.City));

        Assert.Equal(2, d.Lines.Count);
        Assert.Equal("PICK UP (=> USD 84 X 985.14)", d.Lines[0].Description);
        Assert.Equal(82752m, d.Lines[0].UnitPrice);
        Assert.Equal(("UN", 10m, 11208L), (d.Lines[1].Unit, d.Lines[1].DiscountPercent, d.Lines[1].DiscountAmount));

        Assert.Equal(2, d.References.Count);
        Assert.Equal((1, "ANULA DOCUMENTO"), (d.References[1].Code, d.References[1].Reason));
        Assert.Equal(183624, d.Exempt);
        Assert.Equal(183624, d.Total);
        Assert.Equal(0, d.Vat);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<no-es-xml")]
    [InlineData("<Otro/>")]
    public void ReadDocument_SinDocumento_Null(string? xml) => Assert.Null(DteReceivedXmlReader.ReadDocument(xml));
}
