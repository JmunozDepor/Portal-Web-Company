namespace PortalSaas.Abstractions.Contratos.Integraciones;

public interface IIntegrationConnector
{
    string Tipo { get; }

    Task<IReadOnlyList<IntegrationRecord>> PullAsync(
        string conectorConfigJson,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IntegrationPushResult>> PushAsync(
        string conectorConfigJson,
        IReadOnlyList<IntegrationRecord> registros,
        CancellationToken cancellationToken);
}
