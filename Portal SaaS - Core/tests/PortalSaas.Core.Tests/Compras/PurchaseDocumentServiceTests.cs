using System.Text.Json;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Compras;
using Xunit;

namespace PortalSaas.Core.Tests.Compras;

public sealed class PurchaseDocumentServiceTests
{
    private readonly FakeHana _hana = new();
    private readonly FakeSession _session = new();

    private PurchaseDocumentService Service() =>
        new(_hana, new FakeProvider(_session), new AllowDefault(), new DefaultUdf());

    private static PurchaseDocumentDto Document(PurchaseDocumentLineDto? line = null) => new(
        SupplierCardCode: "PN77398220-1",
        SupplierName: null,
        Comments: null,
        DocDate: new DateOnly(2026, 10, 27),
        DocDueDate: new DateOnly(2026, 10, 27),
        TaxDate: new DateOnly(2026, 10, 27),
        SupplierReferenceNumber: null,
        Lines: [line ?? new PurchaseDocumentLineDto(DocumentLineType.Service, null, "Comisión", 1, 1000, 0, null,
            AccountCode: "61-17-001-04", CostCenterCode: "ADM")]);

    private static bool Has(JsonElement body, string property) => body.TryGetProperty(property, out _);

    [Fact]
    public void Catalogo_Facturas_ComparteOpchConObjectCode18()
    {
        foreach (var type in new[] { PurchaseDocumentType.PurchaseInvoice, PurchaseDocumentType.PurchaseReserveInvoice })
        {
            var entry = PurchaseDocumentTypeCatalog.Resolve(type);
            Assert.Equal("OPCH", entry.Table);
            Assert.Equal("PurchaseInvoices", entry.Resource);
            Assert.Equal(18, entry.ObjectCode);
            Assert.True(entry.SupportsCancel);
            Assert.True(entry.DefaultCanCreate);
        }
    }

    [Fact]
    public void Describe_FacturaProveedor_ServicioConDocumentoTributarioYCopiaDePedido() =>
        Assert.Equal(new PurchaseDocumentTypeInfo(DocumentLineType.Service, true, PurchaseDocumentType.PurchaseOrder),
            Service().Describe(PurchaseDocumentType.PurchaseInvoice));

    [Fact]
    public void Describe_FacturaReserva_ArticuloConDocumentoTributarioYCopiaDePedido() =>
        Assert.Equal(new PurchaseDocumentTypeInfo(DocumentLineType.Item, true, PurchaseDocumentType.PurchaseOrder),
            Service().Describe(PurchaseDocumentType.PurchaseReserveInvoice));

    [Fact]
    public void Describe_Pedido_SinParticularidades() =>
        Assert.Equal(new PurchaseDocumentTypeInfo(DocumentLineType.Item, false, null),
            Service().Describe(PurchaseDocumentType.PurchaseOrder));

    [Fact]
    public async Task Create_FacturaReserva_EnviaReserveInvoiceTYesSinIsIns()
    {
        await Service().CreateAsync(PurchaseDocumentType.PurchaseReserveInvoice, "jmunoz", Document());

        var body = _session.Body();
        Assert.Equal("PurchaseInvoices", _session.PostedResource);
        Assert.Equal("tYES", body.GetProperty("ReserveInvoice").GetString());
        Assert.False(Has(body, "isIns"));
    }

    [Fact]
    public async Task Create_FacturaProveedor_NoEnviaMarcaDeReserva()
    {
        await Service().CreateAsync(PurchaseDocumentType.PurchaseInvoice, "jmunoz", Document());

        var body = _session.Body();
        Assert.Equal("PurchaseInvoices", _session.PostedResource);
        Assert.False(Has(body, "ReserveInvoice"));
        Assert.False(Has(body, "isIns"));
    }

    [Fact]
    public async Task Create_DocumentoTributario_EnviaFolioIndicadorYRut()
    {
        var document = Document() with { TaxIndicator = "33", FolioPrefix = "43", FolioNumber = 1157501, FederalTaxId = "77398220-1" };

        await Service().CreateAsync(PurchaseDocumentType.PurchaseInvoice, "jmunoz", document);

        var body = _session.Body();
        Assert.Equal("43", body.GetProperty("FolioPrefixString").GetString());
        Assert.Equal(1157501, body.GetProperty("FolioNumber").GetInt32());
        Assert.Equal("33", body.GetProperty("Indicator").GetString());
        Assert.Equal("77398220-1", body.GetProperty("FederalTaxID").GetString());
    }

    [Fact]
    public async Task Create_Pedido_SinDocumentoTributario_NoEnviaEsasPropiedades()
    {
        await Service().CreateAsync(PurchaseDocumentType.PurchaseOrder, "jmunoz", Document());

        var body = _session.Body();
        Assert.Equal("PurchaseOrders", _session.PostedResource);
        foreach (var property in new[] { "FolioPrefixString", "FolioNumber", "Indicator", "FederalTaxID", "ReserveInvoice" })
        {
            Assert.False(Has(body, property), property);
        }
    }

    [Fact]
    public async Task Create_LineaCopiada_EnviaVinculoAlPedido()
    {
        var line = new PurchaseDocumentLineDto(DocumentLineType.Item, "MOMENTOO-011-7", null, 12, 18.68m, 0, "RECEP",
            BaseType: 22, BaseEntry: 10, BaseLine: 0);

        await Service().CreateAsync(PurchaseDocumentType.PurchaseReserveInvoice, "jmunoz", Document(line));

        var posted = _session.Body().GetProperty("DocumentLines")[0];
        Assert.Equal(22, posted.GetProperty("BaseType").GetInt32());
        Assert.Equal(10, posted.GetProperty("BaseEntry").GetInt32());
        Assert.Equal(0, posted.GetProperty("BaseLine").GetInt32());
    }

    [Fact]
    public async Task List_FacturaReserva_FiltraIsInsY()
    {
        await Service().ListAsync(PurchaseDocumentType.PurchaseReserveInvoice);

        Assert.Contains("""o."isIns" = :extraFilter0""", _hana.LastSql);
        Assert.Equal("Y", _hana.Param("extraFilter0"));
    }

    [Fact]
    public async Task List_FacturaProveedor_FiltraIsInsN()
    {
        await Service().ListAsync(PurchaseDocumentType.PurchaseInvoice);

        Assert.Contains("""o."isIns" = :extraFilter0""", _hana.LastSql);
        Assert.Equal("N", _hana.Param("extraFilter0"));
    }

    [Fact]
    public async Task List_Pedido_SinFiltroIsIns()
    {
        await Service().ListAsync(PurchaseDocumentType.PurchaseOrder);

        Assert.DoesNotContain("isIns", _hana.LastSql);
    }

    [Fact]
    public async Task List_FiltroFolio_BuscaPorNumeroDeFolio()
    {
        await Service().ListAsync(PurchaseDocumentType.PurchaseInvoice, new PurchaseDocumentFilter(Folio: "1157"));

        Assert.Contains("""TO_VARCHAR(o."FolioNum") LIKE :folio""", _hana.LastSql);
        Assert.Equal("%1157%", _hana.Param("folio"));
        Assert.Contains("o.\"FolioPref\" AS \"FolioPrefix\"", _hana.LastSql);
        Assert.Contains("o.\"FolioNum\" AS \"FolioNumber\"", _hana.LastSql);
    }

    [Fact]
    public async Task Get_LeeDocumentoTributario()
    {
        _session.Document = new SapPurchaseDocumentHeader
        {
            DocEntry = 5, DocNum = 10021509, CardCode = "PN77398220-1",
            FolioPrefixString = "43", FolioNumber = 1157501, Indicator = "33", FederalTaxID = "77398220-1",
        };

        var document = await Service().GetAsync(PurchaseDocumentType.PurchaseInvoice, 5);

        Assert.NotNull(document);
        Assert.Equal("43", document!.FolioPrefix);
        Assert.Equal(1157501, document.FolioNumber);
        Assert.Equal("33", document.TaxIndicator);
        Assert.Equal("77398220-1", document.FederalTaxId);
    }
}
