namespace PortalSaas.Abstractions.Contratos.Integraciones;

public interface IIntegrationConnector
{
    string Tipo { get; }

    /// <summary>
    /// cursorIncremental: marca de tiempo de la última corrida Bajada exitosa de esta
    /// IntegrationDefinition (ver IntegrationDefinition.UltimaSincronizacionExitosa), null en la
    /// primera corrida. Un conector que soporte sincronización incremental debe agregar esta
    /// condición al filtro configurado (ver SapDocumentConnector) para no traer siempre el
    /// universo completo de registros que matchean el filtro de negocio.
    /// </summary>
    Task<IReadOnlyList<IntegrationRecord>> PullAsync(
        string conectorConfigJson,
        DateTimeOffset? cursorIncremental,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IntegrationPushResult>> PushAsync(
        string conectorConfigJson,
        IReadOnlyList<IntegrationRecord> registros,
        CancellationToken cancellationToken);

    /// <summary>
    /// Descripción legible de la consulta/llamada que este conector va a ejecutar con la
    /// config dada (recurso + filtro OData para Sap, endpoint + entidad para WmsCloud), para
    /// dejar constancia en IntegrationRunLog.DetalleConsulta de qué se ejecutó realmente --
    /// sin esto, diagnosticar un error de SAP/WMS Cloud requiere leer el código del conector.
    /// No debe lanzar: config inválida devuelve un mensaje descriptivo del problema, nunca
    /// una excepción que tumbe la ejecución real.
    /// </summary>
    string DescribirConsulta(string conectorConfigJson, DateTimeOffset? cursorIncremental = null);
}
