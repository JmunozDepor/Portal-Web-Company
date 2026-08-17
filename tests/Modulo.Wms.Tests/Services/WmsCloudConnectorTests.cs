using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsCloudConnectorTests
{
    private static readonly Guid CompanyIdDePrueba = Guid.NewGuid();

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

    /// <summary>Sin mapeos configurados por defecto -- así los tests existentes (que verifican
    /// el mapeo hardcodeado de siempre) sirven a la vez de regresión "sin config, XML idéntico".</summary>
    private sealed class FakeFieldMappingService : IFieldMappingService
    {
        private readonly IReadOnlyList<WmsFieldMapping> _mapeos;
        public FakeFieldMappingService(IReadOnlyList<WmsFieldMapping>? mapeos = null) => _mapeos = mapeos ?? [];

        public Task<IReadOnlyList<WmsFieldMapping>> ListAllAsync(Guid companyId, CancellationToken ct = default)
            => Task.FromResult(_mapeos);

        public Task<long> CreateAsync(Guid companyId, string mapperKey, string fieldName, string valueTemplate, bool isActive, string updatedBy, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task UpdateAsync(long id, Guid companyId, string valueTemplate, bool isActive, string updatedBy, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeCurrentCompanyAccessor : ICurrentCompanyAccessor
    {
        public Guid CompanyId => CompanyIdDePrueba;
        public string Code => "TEST";
        public string Database => "test";
        public string ServiceLayerUrl => "https://test";
        public string Country => "CL";
        public bool HasCompany => true;
    }

    private static WmsCloudConnector CrearConector(HttpClient httpClient, IReadOnlyList<WmsFieldMapping>? mapeos = null) =>
        new(httpClient, NullLogger<WmsCloudConnector>.Instance, new FakeFieldMappingService(mapeos), new FakeCurrentCompanyAccessor());

    [Fact]
    public async Task PushAsync_ConTipoDocumentoItem_PosteaXmlConNodoItem()
    {
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var conector = CrearConector(httpClient);

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
        var conector = CrearConector(httpClient);

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
        var conector = CrearConector(httpClient);

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

    [Fact]
    public async Task PushAsync_ConTipoDocumentoOrder_PosteaXmlConCabeceraYDetalleAnidados()
    {
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var conector = CrearConector(httpClient);

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "Order",
            ["OrderNbr"] = "VTA-1001",
            ["OrderType"] = "VTA",
            ["OrdDate"] = new DateTime(2026, 8, 16),
            ["ExpDate"] = (DateTime?)null,
            ["ReqShipDate"] = (DateTime?)null,
            ["CustomerPoNbr"] = "PO-1",
            ["ShipToCode"] = "SHIP1",
            ["PickListAbsEntry"] = 100,
            ["BaseObjectType"] = 17,
            ["BaseEntry"] = 500,
            ["CardCode"] = "C001",
            ["CardName"] = "Cliente de prueba",
            ["Lineas"] = new List<IntegrationRecord>
            {
                new(new Dictionary<string, object?> { ["ItemCode"] = "ITM001", ["Quantity"] = 5m, ["LineNum"] = 0, ["SeqNbr"] = 1 }),
            },
        });

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
        var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

        // El contenido posteado es form-urlencoded (xml_data=<xml codificado>) -- se decodifica
        // antes de aserciones que involucran caracteres especiales como '<', '>' o espacios.
        var xmlDecodificado = Uri.UnescapeDataString(handlerFalso.UltimoContenido!.Replace('+', ' '));

        Assert.True(resultado[0].Exito);
        Assert.Contains("ListOfOrders", xmlDecodificado);
        Assert.Contains("VTA-1001", xmlDecodificado);
        Assert.Contains("order_dtl", xmlDecodificado);
        // Fix 1 (ronda de correcciones de revisión final): constantes fijas del legado.
        Assert.Contains("<company_code>DEPOR</company_code>", xmlDecodificado);
        Assert.Contains("<action_code>CREATE</action_code>", xmlDecodificado);
        Assert.Contains("<facility_code>BO02</facility_code>", xmlDecodificado);
        // Fix 2: campos que antes se perdían en la frontera Task 2 -> Task 4 -> Task 5.
        Assert.Contains("<cust_field_2>100</cust_field_2>", xmlDecodificado);
        Assert.Contains("<cust_field_3>Cliente de prueba</cust_field_3>", xmlDecodificado);
        Assert.Contains("<cust_field_4>500</cust_field_4>", xmlDecodificado);
        Assert.Contains("<cust_field_5>17</cust_field_5>", xmlDecodificado);
        Assert.Contains("<cust_short_text_1>100</cust_short_text_1>", xmlDecodificado);
        Assert.Contains("<cust_short_text_2>C001</cust_short_text_2>", xmlDecodificado);
        Assert.Contains("<customer_po_nbr>PO-1</customer_po_nbr>", xmlDecodificado);
        // Fix 4: formato YYYYMMDD, no ISO-8601 con hora.
        Assert.Contains("<ord_date>20260816</ord_date>", xmlDecodificado);
        Assert.DoesNotContain("2026-08-16T00:00:00", xmlDecodificado);
    }

    [Fact]
    public async Task PushAsync_ConMapeoConfigurado_UsaElValorDelTemplateEnVezDelPorDefecto()
    {
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };

        var mapeos = new List<WmsFieldMapping>
        {
            new()
            {
                CompanyId = CompanyIdDePrueba,
                MapperKey = "SAPWMS_ITEM",
                FieldName = "item_alternate_code",
                ValueTemplate = "PREFIX-{ItemCode}",
                IsActive = true,
            },
            // Fila inactiva -- no debe aplicarse, confirma que el filtro IsActive del
            // conector (no solo del reader) se respeta.
            new()
            {
                CompanyId = CompanyIdDePrueba,
                MapperKey = "SAPWMS_ITEM",
                FieldName = "description",
                ValueTemplate = "NO DEBE APARECER",
                IsActive = false,
            },
        };
        var conector = CrearConector(httpClient, mapeos);

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "Item",
            ["ItemCode"] = "ITM001",
            ["ItemName"] = "Artículo de prueba",
            ["BarCode"] = "7801234567890",
        });

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
        var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

        var xmlDecodificado = Uri.UnescapeDataString(handlerFalso.UltimoContenido!.Replace('+', ' '));

        Assert.True(resultado[0].Exito);
        Assert.Contains("<item_alternate_code>PREFIX-ITM001</item_alternate_code>", xmlDecodificado);
        // description sin mapeo activo -> cae al valor por defecto (ItemName), no al literal de la fila inactiva.
        Assert.Contains("<description>Art", xmlDecodificado);
        Assert.DoesNotContain("NO DEBE APARECER", xmlDecodificado);
    }
}
