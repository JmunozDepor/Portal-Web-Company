using PortalSaas.Abstractions.Contratos.Integraciones;

namespace PortalSaas.Integrations.Connectors;

public class SapDocumentConnector : IIntegrationConnector
{
    public string Tipo => "Sap";

    public Task<IReadOnlyList<IntegrationRecord>> PullAsync(
        string conectorConfigJson,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException(
            "SapDocumentConnector.PullAsync pendiente: requiere resolver la dirección de " +
            "dependencia hacia SalesDocumentService/PurchaseDocumentService/InventoryDocumentService " +
            "de PortalSaas.Core (ver nota de arquitectura en el plan de implementación).");
    }

    public Task PushAsync(
        string conectorConfigJson,
        IReadOnlyList<IntegrationRecord> registros,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException(
            "SapDocumentConnector.PushAsync pendiente: requiere resolver la dirección de " +
            "dependencia hacia SalesDocumentService/PurchaseDocumentService/InventoryDocumentService " +
            "de PortalSaas.Core (ver nota de arquitectura en el plan de implementación).");
    }
}
