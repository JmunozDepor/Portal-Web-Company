using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsCloudConnectorTests
{
    private sealed class HttpHandlerFalso : HttpMessageHandler
    {
        public HttpRequestMessage? UltimaRequest { get; private set; }
        public string? UltimoContenido { get; private set; }
        private readonly HttpStatusCode _statusCode;

        public HttpHandlerFalso(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UltimaRequest = request;
            UltimoContenido = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_statusCode) { Content = new StringContent("{}") };
        }
    }

    [Fact]
    public async Task PushAsync_ConTipoDocumentoItem_PosteaXmlConNodoItem()
    {
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var conector = new WmsCloudConnector(httpClient, NullLogger<WmsCloudConnector>.Instance);

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "Item",
            ["ItemCode"] = "ITM001",
            ["ItemName"] = "Artículo de prueba",
            ["BarCode"] = "7801234567890",
        });

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
        var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

        Assert.Single(resultado);
        Assert.True(resultado[0].Exito);
        Assert.Contains("ListOfItems", handlerFalso.UltimoContenido);
        Assert.Contains("ITM001", handlerFalso.UltimoContenido);
    }

    [Fact]
    public async Task PushAsync_WmsRespondeError_MarcaRegistroComoFallido()
    {
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.BadRequest);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var conector = new WmsCloudConnector(httpClient, NullLogger<WmsCloudConnector>.Instance);

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "Item",
            ["ItemCode"] = "ITM001",
            ["ItemName"] = "Artículo de prueba",
            ["BarCode"] = "7801234567890",
        });

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
        var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

        Assert.Single(resultado);
        Assert.False(resultado[0].Exito);
        Assert.NotNull(resultado[0].MensajeError);
    }

    [Fact]
    public async Task PushAsync_ConTipoDocumentoIbShipment_PosteaXmlConDetalleAnidado()
    {
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var conector = new WmsCloudConnector(httpClient, NullLogger<WmsCloudConnector>.Instance);

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "IbShipment",
            ["SapDocEntry"] = 500123,
            ["ShipmentType"] = "TRASLADO_ESTANDAR",
            ["Lineas"] = new List<IntegrationRecord>
            {
                new(new Dictionary<string, object?> { ["ItemCode"] = "ITM001", ["Quantity"] = 10m, ["WhsCode"] = "01", ["LineNum"] = 0 }),
            },
        });

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
        var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

        Assert.True(resultado[0].Exito);
        Assert.Contains("500123", handlerFalso.UltimoContenido);
        Assert.Contains("ib_shipment_dtl", handlerFalso.UltimoContenido);
    }
}
