namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Verifica que UNA cuenta de servicio externo (ExternalServiceProvider) esté operativa
/// -- clave válida, endpoint alcanzable, modelo existente -- SIN consumir cuota de OCR:
/// pega contra el endpoint de metadata de cada proveedor (Gemini: GET del modelo; Azure
/// Document Intelligence: GET /info), que valida credenciales sin procesar ningún
/// documento. Lo dispara el botón "Probar" de Configuración &gt; Proveedores.
///
/// No toca <c>external_service_usages</c>: aísla el problema de credenciales/red del de
/// cuota o de base de datos del módulo.
/// </summary>
public interface IExternalServiceHealthChecker
{
    Task<ServiceHealthResult> CheckAsync(long providerId, Guid companyId, CancellationToken ct = default);
}

/// <summary>Resultado de <see cref="IExternalServiceHealthChecker.CheckAsync"/> -- ok + mensaje listo para mostrar en la UI.</summary>
public sealed record ServiceHealthResult(bool Ok, string Message)
{
    public static ServiceHealthResult Healthy(string message) => new(true, message);
    public static ServiceHealthResult Unhealthy(string message) => new(false, message);
}
