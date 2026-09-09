using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Elige, entre los ExternalServiceProvider activos de una compañía para un
/// ServiceType, cuál usar para la próxima llamada -- probando en orden de Priority y
/// saltando al siguiente si el actual ya agotó su cuota mensual. Centraliza la lógica
/// de fallback multi-proveedor para que AzureMapsRoutingService/
/// AzureDocumentIntelligenceExtractorService no la dupliquen.
/// </summary>
public interface IExternalServiceProviderSelector
{
    /// <summary>
    /// Para servicios de costo FIJO y conocido por llamada (ej. Azure Maps, 1
    /// transacción por request): reserva "quantity" unidades en el primer proveedor
    /// que tenga cupo, probando en orden de prioridad. Null si todos los proveedores
    /// configurados están sin cupo (o no hay ninguno configurado).
    /// </summary>
    Task<SelectedProvider?> SelectForReservationAsync(Guid companyId, string serviceType, int quantity, CancellationToken ct = default);

    /// <summary>
    /// Para servicios cuyo costo real solo se conoce DESPUÉS de la llamada (ej. Azure
    /// Document Intelligence, factura por página analizada del documento): devuelve el
    /// primer proveedor que todavía esté bajo su límite (sin reservar nada -- el
    /// llamador debe registrar el consumo real después con
    /// IExternalServiceUsageService.RecordAsync sobre el mismo ProviderId). Null si
    /// todos están al límite o no hay ninguno configurado.
    /// </summary>
    Task<SelectedProvider?> SelectAvailableAsync(Guid companyId, string serviceType, CancellationToken ct = default);
}

/// <summary>
/// Credenciales ya descifradas de un proveedor elegido -- vive solo en memoria, nunca
/// se persiste así. <see cref="QuotaPeriod"/> (ver Models.QuotaPeriods) lo resuelve el
/// selector a partir del ServiceType para que el extractor pueda registrar el consumo
/// (IExternalServiceUsageService.RecordAsync) contra el balde correcto sin volver a
/// mirar el tipo de servicio.
/// </summary>
public sealed record SelectedProvider(long ProviderId, string? Endpoint, string ApiKey, string QuotaPeriod = QuotaPeriods.Monthly);
