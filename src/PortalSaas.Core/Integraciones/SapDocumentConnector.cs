using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;

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

    public async Task PushAsync(
        string conectorConfigJson,
        IReadOnlyList<IntegrationRecord> registros,
        CancellationToken cancellationToken)
    {
        foreach (var registro in registros)
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
                    throw new NotSupportedException(
                        "SapDocumentConnector.PushAsync para 'Inventory': mapeo DTO pendiente de caso real de " +
                        "negocio. IInventoryDocumentService.CreateAsync está inyectado y listo, pero construir " +
                        "un InventoryDocumentDto a partir de un IntegrationRecord genérico requiere un caso de " +
                        "uso concreto que todavía no existe (ver spec 2026-08-15, punto 'ajuste de alcance').");
                default:
                    throw new NotSupportedException(
                        $"SapDocumentConnector.PushAsync: TipoDocumento '{tipoDocumento}' no reconocido. " +
                        "Valores soportados: 'Sales', 'Purchase', 'Inventory'.");
            }
        }

        await Task.CompletedTask;
    }
}
