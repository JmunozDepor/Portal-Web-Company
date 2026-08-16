using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Integraciones;

public class SapDocumentConnector : IIntegrationConnector
{
    private readonly ISalesDocumentService _salesDocumentService;
    private readonly IPurchaseDocumentService _purchaseDocumentService;
    private readonly IInventoryDocumentService _inventoryDocumentService;
    private readonly ISapConnectionProvider _sapConnectionProvider;

    public SapDocumentConnector(
        ISalesDocumentService salesDocumentService,
        IPurchaseDocumentService purchaseDocumentService,
        IInventoryDocumentService inventoryDocumentService,
        ISapConnectionProvider sapConnectionProvider)
    {
        _salesDocumentService = salesDocumentService;
        _purchaseDocumentService = purchaseDocumentService;
        _inventoryDocumentService = inventoryDocumentService;
        _sapConnectionProvider = sapConnectionProvider;
    }

    public string Tipo => "Sap";

    private sealed record SapWmsOutboundConfig(string TipoEntidad);

    public async Task<IReadOnlyList<IntegrationRecord>> PullAsync(
        string conectorConfigJson,
        CancellationToken cancellationToken)
    {
        var config = System.Text.Json.JsonSerializer.Deserialize<SapWmsOutboundConfig>(conectorConfigJson)
            ?? throw new InvalidOperationException("Config de conector Sap (Bajada) inválida o vacía.");

        var session = await _sapConnectionProvider.GetConnectionAsync(cancellationToken);

        return config.TipoEntidad switch
        {
            "Item" => await LeerItemsAsync(session, cancellationToken),
            "Store" => await LeerStoresAsync(session, cancellationToken),
            "InboundTraslado" => await LeerTrasladosAsync(session, cancellationToken),
            _ => throw new InvalidOperationException($"TipoEntidad '{config.TipoEntidad}' no soportado en PullAsync."),
        };
    }

    private static async Task<IReadOnlyList<IntegrationRecord>> LeerItemsAsync(ISapSession session, CancellationToken ct)
    {
        var filtro = "U_NX_EnviarWMS eq 'Y' and InvntItem eq 'tYES'";
        var filas = await session.GetAsync<List<SapWmsItemRow>>("Items", filtro, ct: ct) ?? [];

        return filas
            .Where(f => !string.IsNullOrWhiteSpace(f.CodeBars) && f.CodeBars != "0")
            .Select(f => new IntegrationRecord(new Dictionary<string, object?>
            {
                ["ItemCode"] = f.ItemCode,
                ["ItemName"] = f.ItemName,
                ["BarCode"] = f.CodeBars,
                ["SourceUpdateDate"] = f.UpdateDate,
            }))
            .ToList();
    }

    private static async Task<IReadOnlyList<IntegrationRecord>> LeerStoresAsync(ISapSession session, CancellationToken ct)
    {
        var filtro = "U_NX_EnviarWMS eq 'Y'";
        var filas = await session.GetAsync<List<SapWmsStoreRow>>("BusinessPartners", filtro, "BPAddresses", ct) ?? [];

        var registros = new List<IntegrationRecord>();
        foreach (var fila in filas)
        {
            var direccionEnvio = fila.BPAddresses?.FirstOrDefault(a => a.AddressType == "bo_ShipTo");
            if (direccionEnvio is null)
            {
                continue;
            }

            registros.Add(new IntegrationRecord(new Dictionary<string, object?>
            {
                ["CardCode"] = fila.CardCode,
                ["CardName"] = fila.CardName,
                ["Street"] = direccionEnvio.Street,
                ["City"] = direccionEnvio.City,
                ["ZipCode"] = direccionEnvio.ZipCode,
                ["SourceUpdateDate"] = fila.UpdateDate,
            }));
        }

        return registros;
    }

    private static async Task<IReadOnlyList<IntegrationRecord>> LeerTrasladosAsync(ISapSession session, CancellationToken ct)
    {
        var filtro = "(U_NX_WMS_SEND eq 'Y' or U_NX_WMS_SEND eq 'EN PROCESO ENVIO WMS' or U_NX_WMS_SEND eq 'EN PROCESO RE-ENVIO WMS') and U_NX_shipment_type ne ''";
        var filas = await session.GetAsync<List<SapWmsTrasladoRow>>("InventoryTransferRequests", filtro, "StockTransferLines", ct) ?? [];

        return filas
            .Select(f => new IntegrationRecord(new Dictionary<string, object?>
            {
                ["SapDocEntry"] = f.DocEntry,
                ["ShipmentType"] = f.U_NX_shipment_type,
                ["SourceUpdateDate"] = f.UpdateDate,
                ["Lineas"] = (f.StockTransferLines ?? [])
                    .Where(l => l.Quantity != 0)
                    .Select((l, indice) => new IntegrationRecord(new Dictionary<string, object?>
                    {
                        ["ItemCode"] = l.ItemCode,
                        ["Quantity"] = l.Quantity,
                        ["WhsCode"] = l.WarehouseCode,
                        ["LineNum"] = indice,
                    }))
                    .ToList(),
            }))
            .ToList();
    }

    public async Task<IReadOnlyList<IntegrationPushResult>> PushAsync(
        string conectorConfigJson,
        IReadOnlyList<IntegrationRecord> registros,
        CancellationToken cancellationToken)
    {
        if (registros.Count == 0)
        {
            return Array.Empty<IntegrationPushResult>();
        }

        var resultados = new List<IntegrationPushResult>();
        var errores = new List<Exception>();

        foreach (var registro in registros)
        {
            try
            {
                var tipoDocumento = registro["TipoDocumento"] as string;

                if (tipoDocumento is null)
                {
                    throw new NotSupportedException(
                        "SapDocumentConnector.PushAsync requiere que el IntegrationRecord traiga un campo " +
                        "'TipoDocumento' ('Sales', 'Purchase' o 'Inventory') para saber a qué document service " +
                        "de SAP enviarlo. Este campo debe venir del IntegrationFieldMapping de la integración.");
                }

                switch (tipoDocumento)
                {
                    case "Sales":
                        throw new NotSupportedException(
                            "SapDocumentConnector.PushAsync para 'Sales': mapeo DTO pendiente de caso real de " +
                            "negocio. ISalesDocumentService.CreateAsync está inyectado y listo, pero construir " +
                            "un SalesDocumentDto a partir de un IntegrationRecord genérico requiere un caso de " +
                            "uso concreto que todavía no existe (ver spec 2026-08-15, punto 'ajuste de alcance').");
                    case "Purchase":
                        throw new NotSupportedException(
                            "SapDocumentConnector.PushAsync para 'Purchase': mapeo DTO pendiente de caso real de " +
                            "negocio. IPurchaseDocumentService.CreateAsync está inyectado y listo, pero construir " +
                            "un PurchaseDocumentDto a partir de un IntegrationRecord genérico requiere un caso de " +
                            "uso concreto que todavía no existe (ver spec 2026-08-15, punto 'ajuste de alcance').");
                    case "Inventory":
                    {
                        var lineasRaw = (List<IntegrationRecord>)(registro["Lineas"] ?? new List<IntegrationRecord>());
                        var lineasDto = lineasRaw.Select(l =>
                        {
                            // Quantity llega como string (shipped_qty de la tabla de staging del
                            // WMS) -- convertirla con Convert.ToDecimal usa la cultura del hilo
                            // actual (cultura del servidor, no necesariamente invariante), mismo
                            // tipo de bug de cultura ya encontrado 2 veces antes en este proyecto
                            // (ver CLAUDE.md). Se parsea explícito en cultura invariante; si no se
                            // puede parsear, esta línea/registro queda como error puntual sin
                            // tumbar el resto del lote.
                            var quantityRaw = l["Quantity"] as string;
                            if (!decimal.TryParse(
                                    quantityRaw,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var cantidad))
                            {
                                throw new FormatException(
                                    $"SapDocumentConnector.PushAsync: no se pudo interpretar 'Quantity' " +
                                    $"('{quantityRaw}') como número invariante para el artículo " +
                                    $"'{l["ItemCode"]}'.");
                            }

                            return new InventoryDocumentLineDto(
                                ItemCode: (string)l["ItemCode"]!,
                                Description: null,
                                Quantity: cantidad,
                                FromWarehouseCode: null,
                                ToWarehouseCode: null,
                                BaseType: (int?)l["BaseType"],
                                BaseEntry: (int?)l["BaseEntry"],
                                BaseLine: (int?)l["BaseLine"]);
                        }).ToList();

                        // DocDate lo produce el reader del WMS (WmsSlshInventoryReader) en
                        // Fields["DocDate"] -- usarlo si vino con valor; si no, mismo fallback de
                        // siempre (fecha actual del servidor).
                        var docDateRaw = registro["DocDate"] as DateTime?;
                        var docDate = docDateRaw.HasValue
                            ? DateOnly.FromDateTime(docDateRaw.Value)
                            : DateOnly.FromDateTime(DateTime.UtcNow);

                        var dto = new InventoryDocumentDto(
                            DocDate: docDate,
                            Comments: null,
                            Lines: lineasDto);

                        await _inventoryDocumentService.CreateAsync(InventoryDocumentType.StockTransfer, "wms-integration", dto, cancellationToken);
                        break;
                    }
                    default:
                        throw new NotSupportedException(
                            $"SapDocumentConnector.PushAsync: TipoDocumento '{tipoDocumento}' no reconocido. " +
                            "Valores soportados: 'Sales', 'Purchase', 'Inventory'.");
                }

                resultados.Add(new IntegrationPushResult(registro, Exito: true, MensajeError: null));
            }
            catch (Exception ex)
            {
                errores.Add(ex);
                resultados.Add(new IntegrationPushResult(registro, Exito: false, MensajeError: ex.Message));
            }
        }

        if (errores.Count == registros.Count)
        {
            // Fallo catastrófico -- todo el lote falló, el llamador (IntegrationSyncHostedService)
            // espera poder ver esto como una excepción para marcar el ciclo entero como Error, no
            // Parcial (mismo comportamiento que tenía este método antes de exponer resultados por
            // registro).
            throw new AggregateException("Todos los registros del lote fallaron.", errores);
        }

        return resultados;
    }
}

internal sealed class SapWmsItemRow
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? CodeBars { get; set; }
    public DateTime UpdateDate { get; set; }
}

internal sealed class SapWmsStoreRow
{
    public string CardCode { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public DateTime UpdateDate { get; set; }
    public List<SapWmsBpAddressRow>? BPAddresses { get; set; }
}

internal sealed class SapWmsBpAddressRow
{
    public string AddressType { get; set; } = string.Empty;
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? ZipCode { get; set; }
}

internal sealed class SapWmsTrasladoRow
{
    public int DocEntry { get; set; }
    public string U_NX_shipment_type { get; set; } = string.Empty;
    public DateTime UpdateDate { get; set; }
    public List<SapWmsTrasladoLineaRow>? StockTransferLines { get; set; }
}

internal sealed class SapWmsTrasladoLineaRow
{
    public string ItemCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
}
