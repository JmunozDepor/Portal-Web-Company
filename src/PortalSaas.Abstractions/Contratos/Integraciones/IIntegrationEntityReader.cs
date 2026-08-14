namespace PortalSaas.Abstractions.Contratos.Integraciones;

public interface IIntegrationEntityReader<T>
{
    string EntidadNegocio { get; }

    Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken);
}
