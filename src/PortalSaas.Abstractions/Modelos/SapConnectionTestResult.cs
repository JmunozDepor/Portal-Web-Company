namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Resultado de probar la conexión real a SAP de una Company -- dos pruebas
/// independientes, porque HanaService (lectura SQL directa) y SapConnectionProvider
/// (Service Layer) son dos caminos completamente distintos con credenciales distintas
/// (usuario técnico de la Instance vs. usuario de integración de la Company) y pueden
/// fallar por separado.
/// </summary>
public sealed record SapConnectionTestResult(
    bool DatabaseSuccess,
    string? DatabaseError,
    bool ServiceLayerSuccess,
    string? ServiceLayerError);
