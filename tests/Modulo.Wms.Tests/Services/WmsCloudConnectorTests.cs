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
        public int CantidadDeRequests { get; private set; }
        private readonly HttpStatusCode _statusCode;

        public HttpHandlerFalso(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CantidadDeRequests++;
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
                ValueTemplate = "PREFIX-{item_alternate_code}",
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

        // Claves snake_case -- así es como WmsSapStageItemReader (Task 8) puebla el
        // registro real para los 3 campos identidad, consistentes con extra_fields.
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "Item",
            ["item_alternate_code"] = "ITM001",
            ["description"] = "Artículo de prueba",
            ["barcode"] = "7801234567890",
        });

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
        var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

        var xmlDecodificado = Uri.UnescapeDataString(handlerFalso.UltimoContenido!.Replace('+', ' '));

        Assert.True(resultado[0].Exito);
        Assert.Contains("<item_alternate_code>PREFIX-ITM001</item_alternate_code>", xmlDecodificado);
        // description sin mapeo activo -> cae al valor por defecto (la propia clave del registro), no al literal de la fila inactiva.
        Assert.Contains("<description>Art", xmlDecodificado);
        Assert.DoesNotContain("NO DEBE APARECER", xmlDecodificado);
    }

    [Fact]
    public async Task PushAsync_ConTipoDocumentoItemYCamposExtra_GeneraUnNodoPorCadaCampo()
    {
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var conector = CrearConector(httpClient);

        // brand_code/putaway_type simulan extra_fields deserializados como JsonElement
        // (Task 8): acá se pasan como string nativo porque IntegrationRecord se construye
        // a mano en el test, pero ArmarNodoItemDinamico debe funcionar igual con ambos.
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "Item",
            ["item_alternate_code"] = "ITM1",
            ["description"] = "Nombre",
            ["barcode"] = "123",
            ["brand_code"] = "NIKE",
            ["putaway_type"] = "A",
            ["_StagingLineIds"] = new List<long> { 1 },
        });

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
        var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

        Assert.True(resultado[0].Exito);
        var xmlDecodificado = Uri.UnescapeDataString(handlerFalso.UltimoContenido!.Replace('+', ' '));

        Assert.Contains("<item_alternate_code>ITM1</item_alternate_code>", xmlDecodificado);
        Assert.Contains("<description>Nombre</description>", xmlDecodificado);
        Assert.Contains("<barcode>123</barcode>", xmlDecodificado);
        Assert.Contains("<brand_code>NIKE</brand_code>", xmlDecodificado);
        Assert.Contains("<putaway_type>A</putaway_type>", xmlDecodificado);
        // Claves internas del motor genérico no deben aparecer como nodos XML.
        Assert.DoesNotContain("TipoDocumento", xmlDecodificado);
        Assert.DoesNotContain("_StagingLineIds", xmlDecodificado);
    }

    [Fact]
    public async Task PushAsync_ConTipoDocumentoStoreYCamposExtra_GeneraUnNodoPorCadaCampoMasCodeYParentCompanyId()
    {
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var conector = CrearConector(httpClient);

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "Store",
            ["PK"] = "SUC001",
            ["name"] = "Sucursal Central",
            ["address_1"] = "Av. Siempre Viva 123",
            ["city"] = "Santiago",
            ["state"] = "RM",
            ["postal_code"] = "8320000",
        });

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
        var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

        Assert.True(resultado[0].Exito);
        var xmlDecodificado = Uri.UnescapeDataString(handlerFalso.UltimoContenido!.Replace('+', ' '));

        Assert.Contains("<code>SUC001</code>", xmlDecodificado);
        Assert.Contains("<parent_company_id>COMP01</parent_company_id>", xmlDecodificado);
        Assert.Contains("<name>Sucursal Central</name>", xmlDecodificado);
        Assert.Contains("<address_1>Av. Siempre Viva 123</address_1>", xmlDecodificado);
        Assert.Contains("<city>Santiago</city>", xmlDecodificado);
        Assert.Contains("<state>RM</state>", xmlDecodificado);
        Assert.Contains("<postal_code>8320000</postal_code>", xmlDecodificado);
    }

    [Fact]
    public async Task PushAsync_ConTipoDocumentoStore_NoIncluyePkComoNodoXmlLiteral()
    {
        // Regresión análoga a SourceUpdateDate en Item: "PK" es la clave de motor usada para
        // calcular "code", no un campo de negocio -- no debe aparecer como nodo <PK> propio.
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var conector = CrearConector(httpClient);

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "Store",
            ["PK"] = "SUC001",
            ["name"] = "Sucursal Central",
        });

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
        var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

        Assert.True(resultado[0].Exito);
        var xmlDecodificado = Uri.UnescapeDataString(handlerFalso.UltimoContenido!.Replace('+', ' '));

        Assert.DoesNotContain("<pk>", xmlDecodificado, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<code>SUC001</code>", xmlDecodificado);
    }

    [Fact]
    public async Task PushAsync_ConTipoDocumentoItemYCampoExtraComoJsonElement_ConvierteAStringLimpio()
    {
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var conector = CrearConector(httpClient);

        // Reproduce el shape real que entrega WmsSapStageItemReader tras deserializar
        // ExtraFieldsJson: los valores llegan como System.Text.Json.JsonElement, no como
        // string/int nativos de C#.
        using var documentoJson = System.Text.Json.JsonDocument.Parse("""{"brand_code":"NIKE","putaway_type":"A"}""");
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "Item",
            ["item_alternate_code"] = "ITM1",
            ["description"] = "Nombre",
            ["barcode"] = "123",
            ["brand_code"] = documentoJson.RootElement.GetProperty("brand_code"),
            ["putaway_type"] = documentoJson.RootElement.GetProperty("putaway_type"),
        });

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
        var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

        Assert.True(resultado[0].Exito);
        var xmlDecodificado = Uri.UnescapeDataString(handlerFalso.UltimoContenido!.Replace('+', ' '));

        // Sin la normalización, un JsonElement de tipo string produciría "NIKE" con comillas
        // de más (p.ej. vía GetRawText/ToString por defecto).
        Assert.Contains("<brand_code>NIKE</brand_code>", xmlDecodificado);
        Assert.Contains("<putaway_type>A</putaway_type>", xmlDecodificado);
        Assert.DoesNotContain("\"NIKE\"", xmlDecodificado);
    }

    [Fact]
    public async Task PushAsync_ConVariosRegistrosYBatchSizeMenorQueLaCantidad_HaceVariosPosts()
    {
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var conector = CrearConector(httpClient);

        var registros = Enumerable.Range(1, 5).Select(i => new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "Item",
            ["ItemCode"] = $"ITM{i:000}",
            ["ItemName"] = $"Articulo {i}",
            ["BarCode"] = "780000000000" + i,
        })).ToList();

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01","BatchSize":2}""";
        var resultado = await conector.PushAsync(config, registros, CancellationToken.None);

        Assert.Equal(5, resultado.Count);
        Assert.All(resultado, r => Assert.True(r.Exito));
        // BatchSize=2 sobre 5 registros -> 3 POSTs (2+2+1). El handler falso solo guarda
        // el ULTIMO request/contenido, así que se cuenta a través de un contador propio.
        Assert.Equal(3, handlerFalso.CantidadDeRequests);
    }

    [Fact]
    public async Task PushAsync_ConVariosRegistrosYBatchSizeSuficiente_HaceUnSoloPostConVariosNodos()
    {
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var conector = CrearConector(httpClient);

        var registros = Enumerable.Range(1, 3).Select(i => new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "Item",
            ["ItemCode"] = $"ITM{i:000}",
            ["ItemName"] = $"Articulo {i}",
            ["BarCode"] = "780000000000" + i,
        })).ToList();

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01","BatchSize":50}""";
        var resultado = await conector.PushAsync(config, registros, CancellationToken.None);

        Assert.Equal(3, resultado.Count);
        Assert.Equal(1, handlerFalso.CantidadDeRequests);
        var xmlDecodificado = Uri.UnescapeDataString(handlerFalso.UltimoContenido!.Replace('+', ' '));
        Assert.Contains("ITM001", xmlDecodificado);
        Assert.Contains("ITM002", xmlDecodificado);
        Assert.Contains("ITM003", xmlDecodificado);
    }
}
