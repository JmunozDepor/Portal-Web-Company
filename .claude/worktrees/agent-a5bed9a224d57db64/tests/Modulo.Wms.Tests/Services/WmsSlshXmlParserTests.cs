using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSlshXmlParserTests
{
    private const string XmlDeEjemplo = @"
        <Message>
          <Header>
            <MessageId>MSG-1</MessageId>
            <Entity>shipped_load</Entity>
          </Header>
          <load>
            <order_type>NORMAL</order_type>
            <dest_facility_code>WH01</dest_facility_code>
          </load>
          <ob_stop>
            <ob_lpn_nbr>LPN-001</ob_lpn_nbr>
            <item_part_a>ITEM-A</item_part_a>
          </ob_stop>
          <ob_stop>
            <ob_lpn_nbr>LPN-002</ob_lpn_nbr>
            <item_part_a>ITEM-B</item_part_a>
          </ob_stop>
        </Message>";

    [Fact]
    public void Parse_ConDosObStop_RetornaDosFilas()
    {
        var filas = WmsSlshXmlParser.Parse(XmlDeEjemplo);

        Assert.Equal(2, filas.Count);
    }

    [Fact]
    public void Parse_TomaCamposDeObStopPrimero()
    {
        var filas = WmsSlshXmlParser.Parse(XmlDeEjemplo);

        Assert.Equal("LPN-001", filas[0].ob_lpn_nbr);
        Assert.Equal("ITEM-A", filas[0].item_part_a);
        Assert.Equal("LPN-002", filas[1].ob_lpn_nbr);
    }

    [Fact]
    public void Parse_CaeAloadCuandoNoEstaEnObStop()
    {
        var filas = WmsSlshXmlParser.Parse(XmlDeEjemplo);

        Assert.Equal("NORMAL", filas[0].order_type);
        Assert.Equal("WH01", filas[0].dest_facility_code);
    }

    [Fact]
    public void Parse_CaeAHeaderCuandoNoEstaEnObStopNiEnLoad()
    {
        var filas = WmsSlshXmlParser.Parse(XmlDeEjemplo);

        Assert.Equal("MSG-1", filas[0].MessageId);
    }

    [Fact]
    public void Parse_SinNodosObStop_RetornaListaVacia()
    {
        const string xmlSinStops = @"<Message><Header><MessageId>X</MessageId><Entity>shipped_load</Entity></Header><load></load></Message>";

        var filas = WmsSlshXmlParser.Parse(xmlSinStops);

        Assert.Empty(filas);
    }
}
