namespace PortalSaas.Abstractions.Contratos.Integraciones;

public interface IIntegrationEntityReader
{
    string EntidadNegocio { get; }

    Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken);
    Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken);
}
