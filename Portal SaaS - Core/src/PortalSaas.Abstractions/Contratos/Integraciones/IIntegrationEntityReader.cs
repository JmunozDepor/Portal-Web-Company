namespace PortalSaas.Abstractions.Contratos.Integraciones;

public interface IIntegrationEntityReader
{
    string EntidadNegocio { get; }

    /// <summary>
    /// limiteMaximo: tope de filas Pendiente a traer en esta corrida, tomado de la config del
    /// conector de la IntegrationDefinition (ej. WmsCloudConfig.MaxRecordsPerCycle) -- null si
    /// no se configuró, en cuyo caso el reader usa su propio default conservador. Existe para
    /// que un backlog grande no haga que la corrida completa exceda
    /// IntegrationSyncHostedService.TimeoutPorIntegracion (ver doc-comment de
    /// WmsSapStageItemReader.MaximoPorCiclo para el incidente real que lo motivó).
    /// </summary>
    Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, int? limiteMaximo, CancellationToken cancellationToken);
    Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken);
}
