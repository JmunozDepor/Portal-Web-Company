using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Catalogos;
using PortalSaas.Core.Compras;
using Xunit;

namespace PortalSaas.Core.Tests.Compras;

public sealed class PurchaseCopyFromQueriesTests
{
    private readonly FakeHana _hana = new();

    private PurchaseDocumentService Service() =>
        new(_hana, new FakeProvider(new FakeSession()), new AllowDefault(), new DefaultUdf());

    [Fact]
    public async Task ListOpenOrders_FiltraProveedorAbiertosYLineasPendientes()
    {
        await Service().ListOpenOrdersAsync("PE71506673");

        Assert.Equal("PE71506673", _hana.Param("cardCode"));
        Assert.Contains("FROM \"OPOR\"", _hana.LastSql);
        Assert.Contains("\"POR1\"", _hana.LastSql);
        Assert.Contains("o.\"DocStatus\" = 'O'", _hana.LastSql);
        Assert.Contains("o.\"CANCELED\" = 'N'", _hana.LastSql);
        Assert.Contains("l.\"LineStatus\" = 'O'", _hana.LastSql);
        Assert.Contains("l.\"OpenQty\" > 0", _hana.LastSql);
    }

    [Fact]
    public async Task GetOpenOrderLines_SinPedidos_NoConsulta()
    {
        var lines = await Service().GetOpenOrderLinesAsync([]);

        Assert.Empty(lines);
        Assert.Null(_hana.LastSql);
    }

    [Fact]
    public async Task GetOpenOrderLines_PideCadaPedidoComoParametro()
    {
        await Service().GetOpenOrderLinesAsync([10, 11]);

        Assert.Contains("IN (:docEntry0, :docEntry1)", _hana.LastSql);
        Assert.Equal(10, _hana.Param("docEntry0"));
        Assert.Equal(11, _hana.Param("docEntry1"));
        Assert.Contains("l.\"PriceBefDi\" AS \"UnitPrice\"", _hana.LastSql);
        Assert.Contains("l.\"OpenQty\" AS \"OpenQuantity\"", _hana.LastSql);
    }

    [Fact]
    public async Task GetOpenOrderLines_CalculaTipoDeLinea()
    {
        _hana.With(
            new PurchaseOpenOrderLineDto { DocEntry = 10, DocType = "S", CardCode = "P1" },
            new PurchaseOpenOrderLineDto { DocEntry = 11, DocType = "I", CardCode = "P1" });

        var lines = await Service().GetOpenOrderLinesAsync([10, 11]);

        Assert.Equal(DocumentLineType.Service, lines[0].LineType);
        Assert.Equal(DocumentLineType.Item, lines[1].LineType);
    }

    [Fact]
    public async Task FindTaxFolio_BuscaFacturasNoCanceladasDelProveedor()
    {
        Assert.Null(await Service().FindTaxFolioAsync("PN77398220-1", "43", 1157501));
        Assert.Contains("FROM \"OPCH\"", _hana.LastSql);
        Assert.Contains("o.\"CANCELED\" = 'N'", _hana.LastSql);
        Assert.Contains("o.\"FolioPref\" = :folioPrefix", _hana.LastSql);
        Assert.Contains("o.\"FolioNum\" = :folioNumber", _hana.LastSql);
        Assert.Equal("PN77398220-1", _hana.Param("cardCode"));
        Assert.Equal("43", _hana.Param("folioPrefix"));
        Assert.Equal(1157501, _hana.Param("folioNumber"));

        _hana.With(10021509);
        Assert.Equal(10021509, await Service().FindTaxFolioAsync("PN77398220-1", "43", 1157501));
    }

    [Fact]
    public async Task IndicatorCatalog_LeeOidc()
    {
        _hana.With(new DocumentIndicatorDto { Code = "33", Name = "Factura Electrónica" });

        var indicators = await new DocumentIndicatorCatalogService(_hana).ListAsync();

        Assert.Contains("FROM \"OIDC\"", _hana.LastSql);
        Assert.Equal("Factura Electrónica", Assert.Single(indicators).Name);
    }

    [Fact]
    public async Task PartnerDefaults_TraeRutDelProveedor()
    {
        await new BusinessPartnerDefaultsService(_hana).GetAsync("PN77398220-1");

        Assert.Contains("\"LicTradNum\" AS \"FederalTaxId\"", _hana.LastSql);
    }
}
