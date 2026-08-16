using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Conector del motor genérico (IIntegrationConnector) que postea documentos reales a
/// Oracle WMS Cloud (LogFire), mismo formato XML descubierto en el legado
/// (WmsApiService.SendXmlWithResponseAsync, C:\PROYECTOS\WMS_Suite): POST
/// form-urlencoded con "xml_data" y auth Basic. La estructura raíz (LgfData/Header) es
/// fija; la lista (ListOfItems/ListOfStores/ListOfIbShipments) depende de
/// Fields["TipoDocumento"] de cada registro -- un lote nunca mezcla tipos porque cada
/// IntegrationDefinition de Subida es de una sola entidad (ver Task 5).
///
/// Vocabulario de nodo XML CONFIRMADO contra el DDL real de staging del legado
/// (C:\PROYECTOS\WMS_Suite\db\provisioning\hana\007_stg_sap_products.sql,
/// 009_stg_sap_store.sql, 012_stg_sap_ib_shipment_hdr.sql,
/// 013_stg_sap_ib_shipment_dtl.sql -- "DDL tomado del volcado real de producción" según
/// el comentario de cada archivo) -- el legado arma cada nodo dinámicamente con
/// `new XElement(kvp.Key.ToLower(), valor)` a partir de esos nombres de columna, así que
/// esos nombres SON el vocabulario real que espera Oracle WMS Cloud:
///   - Item (STG_SAP_PRODUCTS): item_alternate_code, description, barcode (NO "bar_code"
///     como se había asumido en el diseño inicial de Task 1/3 -- corregido acá).
///   - Store (STG_SAP_STORE): code, name (coincide con lo ya asumido).
///   - Traslado / IbShipment cabecera (STG_SAP_IB_SHIPMENT_HDR): shipment_nbr,
///     shipment_type (coincide).
///   - Traslado / IbShipment detalle (STG_SAP_IB_SHIPMENT_DTL): seq_nbr,
///     item_alternate_code, shipped_qty (NO "expected_qty" como se había asumido),
///     facility_code (NO "dest_facility_code" -- esa columna no existe en el DDL real,
///     la tabla solo tiene "facility_code", igual que la cabecera).
///   - Order / Picking cabecera (order_hdr): order_nbr, order_type, ord_date, exp_date,
///     req_ship_date, ref_nbr (mapeado desde CustomerPoNbr), dest_dept_nbr (mapeado desde
///     ShipToCode), priority (fijo "1" -- sin fuente en SAP para este campo, ver spec de
///     Ronda D).
///   - Order / Picking detalle (order_dtl): order_nbr, seq_nbr, item_alternate_code,
///     ord_qty (mapeado desde Quantity).
/// </summary>
public class WmsCloudConnector : IIntegrationConnector
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WmsCloudConnector> _logger;

    public WmsCloudConnector(HttpClient httpClient, ILogger<WmsCloudConnector> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string Tipo => "WmsCloud";

    public Task<IReadOnlyList<IntegrationRecord>> PullAsync(string conectorConfigJson, CancellationToken cancellationToken) =>
        throw new NotSupportedException("WmsCloudConnector.PullAsync no está implementado -- este conector solo envía (Subida), nunca lee de Oracle WMS Cloud.");

    public async Task<IReadOnlyList<IntegrationPushResult>> PushAsync(
        string conectorConfigJson,
        IReadOnlyList<IntegrationRecord> registros,
        CancellationToken cancellationToken)
    {
        if (registros.Count == 0)
        {
            return Array.Empty<IntegrationPushResult>();
        }

        var config = JsonSerializer.Deserialize<WmsCloudConfig>(conectorConfigJson)
            ?? throw new InvalidOperationException("Config de conector WmsCloud inválida o vacía.");

        // A diferencia de SapDocumentConnector.PushAsync (que relanza AggregateException
        // cuando TODO el lote falla), acá NUNCA se relanza -- se documentó ya en
        // IntegrationDefinition que "en la práctica cada IntegrationDefinition de Subida
        // es de una sola entidad" (ver docs de la Ronda C), así que un lote de WmsCloud
        // casi siempre tiene tamaño 1: aplicar el mismo criterio "lanzar si el 100% del
        // lote falló" degeneraría en "siempre lanzar cuando falla", perdiendo el
        // aislamiento por registro (ack individual vía IntegrationPushResult) que es
        // justamente el propósito del contrato. Cada fallo, sea de 1 o de N registros,
        // se reporta siempre como IntegrationPushResult(Exito: false) -- el llamador
        // (IntegrationSyncHostedService) ya hace MarcarProcesadoAsync por resultado.
        var resultados = new List<IntegrationPushResult>();

        foreach (var registro in registros)
        {
            try
            {
                var xml = ArmarXml(registro, config);
                await EnviarAsync(xml, config, cancellationToken);
                resultados.Add(new IntegrationPushResult(registro, Exito: true, MensajeError: null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando documento a Oracle WMS Cloud");
                resultados.Add(new IntegrationPushResult(registro, Exito: false, MensajeError: ex.Message));
            }
        }

        return resultados;
    }

    private static XDocument ArmarXml(IntegrationRecord registro, WmsCloudConfig config)
    {
        var tipoDocumento = (string)registro["TipoDocumento"]!;
        var (entity, nombreLista, nombreItem) = tipoDocumento switch
        {
            "Item" => ("item", "ListOfItems", "item"),
            "Store" => ("store", "ListOfStores", "store"),
            "IbShipment" => ("ib_shipment", "ListOfIbShipments", "ib_shipment"),
            "Order" => ("order", "ListOfOrders", "order"),
            _ => throw new InvalidOperationException($"TipoDocumento '{tipoDocumento}' no soportado en WmsCloudConnector."),
        };

        var header = new XElement("Header",
            new XElement("DocumentVersion", "24D"),
            new XElement("OriginSystem", "LogFire"),
            new XElement("ClientEnvCode", config.ClientEnvCode),
            new XElement("ParentCompanyCode", config.ParentCompanyCode),
            new XElement("Entity", entity),
            new XElement("TimeStamp", DateTime.UtcNow.ToString("O")),
            new XElement("MessageId", Guid.NewGuid().ToString()));

        var nodoItem = tipoDocumento switch
        {
            "Item" => new XElement(nombreItem,
                new XElement("item_alternate_code", registro["ItemCode"]),
                new XElement("description", registro["ItemName"]),
                new XElement("barcode", registro["BarCode"])),
            "Store" => new XElement(nombreItem,
                new XElement("code", registro["CardCode"]),
                new XElement("name", registro["CardName"]),
                new XElement("parent_company_id", config.ParentCompanyCode)),
            "IbShipment" => ArmarNodoIbShipment(registro),
            "Order" => ArmarNodoOrder(registro),
            _ => throw new InvalidOperationException($"TipoDocumento '{tipoDocumento}' no soportado en WmsCloudConnector."),
        };

        return new XDocument(new XElement("LgfData", header, new XElement(nombreLista, nodoItem)));
    }

    private static XElement ArmarNodoIbShipment(IntegrationRecord registro)
    {
        var lineas = (List<IntegrationRecord>)registro["Lineas"]!;
        var hdr = new XElement("ib_shipment_hdr",
            new XElement("shipment_nbr", registro["SapDocEntry"]),
            new XElement("shipment_type", registro["ShipmentType"]));

        var detalles = lineas.Select(l => new XElement("ib_shipment_dtl",
            new XElement("seq_nbr", l["LineNum"]),
            new XElement("item_alternate_code", l["ItemCode"]),
            new XElement("shipped_qty", l["Quantity"]),
            new XElement("facility_code", l["WhsCode"])));

        return new XElement("ib_shipment", hdr, detalles);
    }

    private static XElement ArmarNodoOrder(IntegrationRecord registro)
    {
        var lineas = (List<IntegrationRecord>)registro["Lineas"]!;
        var hdr = new XElement("order_hdr",
            new XElement("order_nbr", registro["OrderNbr"]),
            new XElement("order_type", registro["OrderType"]),
            new XElement("ord_date", registro["OrdDate"]),
            new XElement("exp_date", registro["ExpDate"]),
            new XElement("req_ship_date", registro["ReqShipDate"]),
            new XElement("ref_nbr", registro["CustomerPoNbr"]),
            new XElement("dest_dept_nbr", registro["ShipToCode"]),
            new XElement("priority", "1"));

        var detalles = lineas.Select(l => new XElement("order_dtl",
            new XElement("order_nbr", registro["OrderNbr"]),
            new XElement("seq_nbr", l["SeqNbr"]),
            new XElement("item_alternate_code", l["ItemCode"]),
            new XElement("ord_qty", l["Quantity"])));

        return new XElement("order", hdr, detalles);
    }

    private async Task EnviarAsync(XDocument xml, WmsCloudConfig config, CancellationToken cancellationToken)
    {
        var authToken = Encoding.UTF8.GetBytes($"{config.Usuario}:{config.Clave}");
        using var request = new HttpRequestMessage(HttpMethod.Post, config.ApiUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["xml_data"] = xml.ToString() }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var cuerpo = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Oracle WMS Cloud respondió {(int)response.StatusCode}: {cuerpo}");
        }
    }

    private sealed record WmsCloudConfig(string ApiUrl, string Usuario, string Clave, string ClientEnvCode, string ParentCompanyCode);
}
