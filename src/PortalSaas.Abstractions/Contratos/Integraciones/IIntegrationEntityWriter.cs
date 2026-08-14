namespace PortalSaas.Abstractions.Contratos.Integraciones;

public interface IIntegrationEntityWriter<T>
{
    string EntidadNegocio { get; }

    Task EscribirAsync(Guid companyId, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken);
}
