using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
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
    private readonly IFieldMappingService _fieldMappingService;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public WmsCloudConnector(
        HttpClient httpClient,
        ILogger<WmsCloudConnector> logger,
        IFieldMappingService fieldMappingService,
        ICurrentCompanyAccessor currentCompany)
    {
        _httpClient = httpClient;
        _logger = logger;
        _fieldMappingService = fieldMappingService;
        _currentCompany = currentCompany;
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

        // Se cargan una sola vez por lote (no por registro) -- una IntegrationDefinition
        // de Subida es siempre de una sola compañía, ver comentario más abajo. Sin filas
        // activas para un campo, ArmarXml cae al mismo mapeo hardcodeado de siempre (ver
        // docs/superpowers/specs/2026-08-16-mapeo-campos-sap-wms-design.md).
        var mapeos = (await _fieldMappingService.ListAllAsync(_currentCompany.CompanyId, cancellationToken))
            .Where(m => m.IsActive)
            .ToDictionary(m => (m.MapperKey, m.FieldName), m => m.ValueTemplate);

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
                var xml = ArmarXml(registro, config, mapeos);
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

    private static XDocument ArmarXml(IntegrationRecord registro, WmsCloudConfig config, IReadOnlyDictionary<(string MapperKey, string FieldName), string> mapeos)
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
                CampoXml(mapeos, "SAPWMS_ITEM", "item_alternate_code", registro, registro["ItemCode"]),
                CampoXml(mapeos, "SAPWMS_ITEM", "description", registro, registro["ItemName"]),
                CampoXml(mapeos, "SAPWMS_ITEM", "barcode", registro, registro["BarCode"])),
            "Store" => new XElement(nombreItem,
                CampoXml(mapeos, "SAPWMS_STORE", "code", registro, registro["CardCode"]),
                CampoXml(mapeos, "SAPWMS_STORE", "name", registro, registro["CardName"]),
                CampoXml(mapeos, "SAPWMS_STORE", "parent_company_id", registro, config.ParentCompanyCode)),
            "IbShipment" => ArmarNodoIbShipment(registro, mapeos),
            "Order" => ArmarNodoOrder(registro, mapeos),
            _ => throw new InvalidOperationException($"TipoDocumento '{tipoDocumento}' no soportado en WmsCloudConnector."),
        };

        return new XDocument(new XElement("LgfData", header, new XElement(nombreLista, nodoItem)));
    }

    /// <summary>
    /// Si hay una fila activa en wms_oracle_field_mappings para (mapperKey, fieldName),
    /// resuelve su ValueTemplate contra registro; si no, usa valorPorDefecto -- el mismo
    /// valor que este campo tenía hardcodeado antes de esta entrega, así que sin
    /// configuración nueva el XML sale idéntico al de siempre.
    /// </summary>
    private static XElement CampoXml(
        IReadOnlyDictionary<(string MapperKey, string FieldName), string> mapeos,
        string mapperKey, string fieldName, IntegrationRecord registro, object? valorPorDefecto)
    {
        if (mapeos.TryGetValue((mapperKey, fieldName), out var template))
        {
            return new XElement(fieldName, WmsFieldTemplateResolver.Resolve(template, registro));
        }
        return new XElement(fieldName, valorPorDefecto);
    }

    private static XElement ArmarNodoIbShipment(IntegrationRecord registro, IReadOnlyDictionary<(string MapperKey, string FieldName), string> mapeos)
    {
        var lineas = (List<IntegrationRecord>)registro["Lineas"]!;
        var hdr = new XElement("ib_shipment_hdr",
            CampoXml(mapeos, "SAPWMS_INBOUND_HDR", "shipment_nbr", registro, registro["SapDocEntry"]),
            CampoXml(mapeos, "SAPWMS_INBOUND_HDR", "shipment_type", registro, registro["ShipmentType"]));

        var detalles = lineas.Select(l => new XElement("ib_shipment_dtl",
            CampoXml(mapeos, "SAPWMS_INBOUND_DTL", "seq_nbr", l, l["LineNum"]),
            CampoXml(mapeos, "SAPWMS_INBOUND_DTL", "item_alternate_code", l, l["ItemCode"]),
            CampoXml(mapeos, "SAPWMS_INBOUND_DTL", "shipped_qty", l, l["Quantity"]),
            CampoXml(mapeos, "SAPWMS_INBOUND_DTL", "facility_code", l, l["WhsCode"])));

        return new XElement("ib_shipment", hdr, detalles);
    }

    private static XElement ArmarNodoOrder(IntegrationRecord registro, IReadOnlyDictionary<(string MapperKey, string FieldName), string> mapeos)
    {
        var lineas = (List<IntegrationRecord>)registro["Lineas"]!;
        var hdr = new XElement("order_hdr",
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "company_code", registro, "DEPOR"),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "action_code", registro, "CREATE"),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "facility_code", registro, "BO02"),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "order_nbr", registro, registro["OrderNbr"]),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "order_type", registro, registro["OrderType"]),
            new XElement("ord_date", FormatearFecha(registro["OrdDate"])),
            new XElement("exp_date", FormatearFecha(registro["ExpDate"])),
            new XElement("req_ship_date", FormatearFecha(registro["ReqShipDate"])),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "ref_nbr", registro, registro["CustomerPoNbr"]),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "dest_dept_nbr", registro, registro["ShipToCode"]),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "priority", registro, "1"),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "cust_field_2", registro, registro["PickListAbsEntry"]),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "cust_field_3", registro, registro["CardName"]),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "cust_field_4", registro, registro["BaseEntry"]),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "cust_field_5", registro, registro["BaseObjectType"]),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "cust_short_text_1", registro, registro["PickListAbsEntry"]),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "cust_short_text_2", registro, registro["CardCode"]),
            CampoXml(mapeos, "SAPWMS_ORDER_HDR", "customer_po_nbr", registro, registro["CustomerPoNbr"]));

        var detalles = lineas.Select(l => new XElement("order_dtl",
            CampoXml(mapeos, "SAPWMS_ORDER_DTL", "order_nbr", l, registro["OrderNbr"]),
            CampoXml(mapeos, "SAPWMS_ORDER_DTL", "seq_nbr", l, l["SeqNbr"]),
            CampoXml(mapeos, "SAPWMS_ORDER_DTL", "item_alternate_code", l, l["ItemCode"]),
            CampoXml(mapeos, "SAPWMS_ORDER_DTL", "ord_qty", l, l["Quantity"])));

        return new XElement("order", hdr, detalles);
    }

    /// <summary>
    /// El legado usa formato YYYYMMDD (TO_DATS(...) en el SP legado) para las fechas del
    /// order_hdr, no ISO-8601 con hora que produce XElement al recibir un DateTime
    /// directamente. Si el valor es null, se devuelve null -- XElement(nombre, (object?)null)
    /// ya produce un nodo vacío sin valor, mismo criterio que usan hoy los demás campos
    /// opcionales de este archivo (ej. ExpDate/ReqShipDate cuando vienen null).
    /// </summary>
    private static string? FormatearFecha(object? valor) => valor is DateTime fecha ? fecha.ToString("yyyyMMdd") : null;

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
