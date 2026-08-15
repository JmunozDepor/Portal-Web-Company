using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Integraciones;

public class SapDocumentConnector : IIntegrationConnector
{
    private readonly ISalesDocumentService _salesDocumentService;
    private readonly IPurchaseDocumentService _purchaseDocumentService;
    private readonly IInventoryDocumentService _inventoryDocumentService;

    public SapDocumentConnector(
        ISalesDocumentService salesDocumentService,
        IPurchaseDocumentService purchaseDocumentService,
        IInventoryDocumentService inventoryDocumentService)
    {
        _salesDocumentService = salesDocumentService;
        _purchaseDocumentService = purchaseDocumentService;
        _inventoryDocumentService = inventoryDocumentService;
    }

    public string Tipo => "Sap";

    public Task<IReadOnlyList<IntegrationRecord>> PullAsync(
        string conectorConfigJson,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException(
            "SapDocumentConnector.PullAsync no está implementado: la bajada (descarga desde SAP) " +
            "está explícitamente fuera de alcance del motor de integración por ahora.");
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
