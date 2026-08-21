using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSvshXmlParserTests
{
    private const string XmlDosShipmentsUnDetalleCadaUno = """
        <Message>
          <Header>
            <DocumentVersion>1.0</DocumentVersion>
            <OriginSystem>WMS</OriginSystem>
            <ClientEnvCode>CLIENT_TEST</ClientEnvCode>
          </Header>
          <ib_shipment>
            <ib_shipment_hdr>
              <shipment_nbr>ASN1001</shipment_nbr>
              <facility_code>BOD1</facility_code>
            </ib_shipment_hdr>
            <ib_shipment_dtl>
              <shipment_dtl_cust_field_1>1250000001</shipment_dtl_cust_field_1>
              <shipment_dtl_cust_field_2>555</shipment_dtl_cust_field_2>
              <item_part_a>ITEM-A</item_part_a>
              <received_qty>10</received_qty>
            </ib_shipment_dtl>
          </ib_shipment>
          <ib_shipment>
            <ib_shipment_hdr>
              <shipment_nbr>ASN1002</shipment_nbr>
              <facility_code>BOD1</facility_code>
            </ib_shipment_hdr>
            <ib_shipment_dtl>
              <shipment_dtl_cust_field_1>1250000001</shipment_dtl_cust_field_1>
              <shipment_dtl_cust_field_2>556</shipment_dtl_cust_field_2>
              <item_part_a>ITEM-B</item_part_a>
              <received_qty>5</received_qty>
            </ib_shipment_dtl>
          </ib_shipment>
        </Message>
        """;

    [Fact]
    public void Parse_DosShipments_DevuelveUnaFilaPorLineaDeDetalle()
    {
        var filas = WmsSvshXmlParser.Parse(XmlDosShipmentsUnDetalleCadaUno);

        Assert.Equal(2, filas.Count);
        Assert.Contains(filas, f => f.shipment_nbr == "ASN1001" && f.item_part_a == "ITEM-A" && f.received_qty == "10");
        Assert.Contains(filas, f => f.shipment_nbr == "ASN1002" && f.item_part_a == "ITEM-B" && f.received_qty == "5");
    }

    [Fact]
    public void Parse_CampoSoloEnHeaderGlobal_SePropagaATodasLasFilas()
    {
        var filas = WmsSvshXmlParser.Parse(XmlDosShipmentsUnDetalleCadaUno);

        Assert.All(filas, f => Assert.Equal("CLIENT_TEST", f.ClientEnvCode));
    }

    [Fact]
    public void Parse_CampoEnDetalleTienePrioridadSobreHeaderDelShipment()
    {
        const string xml = """
            <Message>
              <Header></Header>
              <ib_shipment>
                <ib_shipment_hdr>
                  <shipment_nbr>ASN-PRIORIDAD</shipment_nbr>
                </ib_shipment_hdr>
                <ib_shipment_dtl>
                  <shipment_nbr>ASN-DETALLE-GANA</shipment_nbr>
                  <item_part_a>ITEM-X</item_part_a>
                </ib_shipment_dtl>
              </ib_shipment>
            </Message>
            """;

        var filas = WmsSvshXmlParser.Parse(xml);

        Assert.Single(filas);
        Assert.Equal("ASN-DETALLE-GANA", filas[0].shipment_nbr);
    }

    [Fact]
    public void Parse_SinNodosIbShipmentDtl_DevuelveListaVacia()
    {
        const string xml = "<Message><Header></Header></Message>";

        var filas = WmsSvshXmlParser.Parse(xml);

        Assert.Empty(filas);
    }
}
