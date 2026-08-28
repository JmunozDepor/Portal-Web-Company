namespace Modulo.Rendiciones.Models;

/// <summary>
/// Consumo mensual de un ExternalServiceProvider concreto -- una fila por
/// (ProviderId, Year, Month). Antes se trackeaba por (CompanyId, ServiceName), un solo
/// contador por servicio; con múltiples proveedores por servicio (ver
/// ExternalServiceProvider) cada cuenta lleva su propio contador, para poder saber
/// cuál está disponible y cuál ya agotó su cuota. Reserva atómica vía
/// IExternalServiceUsageService.TryReserveAsync antes de una llamada de costo fijo
/// conocido, o registro simple después vía RecordAsync cuando el costo real solo se
/// conoce al terminar.
/// </summary>
public class ExternalServiceUsage
{
    public long Id { get; set; }

    public required long ProviderId { get; set; }

    public ExternalServiceProvider? Provider { get; set; }

    public required int Year { get; set; }

    public required int Month { get; set; }

    public int UsedUnits { get; set; }
}
