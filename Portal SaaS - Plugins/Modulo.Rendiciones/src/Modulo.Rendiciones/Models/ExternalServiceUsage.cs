namespace Modulo.Rendiciones.Models;

/// <summary>
/// Consumo de un ExternalServiceProvider concreto en un período de cuota -- una fila por
/// (ProviderId, Year, Month, Day). <see cref="Day"/> = 0 para proveedores de cuota
/// MENSUAL (Azure Maps / Azure Document Intelligence): la fila es todo el mes. Para
/// proveedores de cuota DIARIA (Google Gemini) <see cref="Day"/> = 1..31 y hay una fila
/// por día. El día se calcula en hora del Pacífico (UTC-8), que es cuando Google
/// reinicia el cupo diario gratuito -- ver ExternalServiceUsageService.
///
/// Antes se trackeaba por (CompanyId, ServiceName), un solo contador por servicio; con
/// múltiples proveedores por servicio (ver ExternalServiceProvider) cada cuenta lleva su
/// propio contador. Reserva atómica vía IExternalServiceUsageService.TryReserveAsync
/// antes de una llamada de costo fijo conocido, o registro simple después vía
/// RecordAsync cuando el costo real solo se conoce al terminar.
/// </summary>
public class ExternalServiceUsage
{
    public long Id { get; set; }

    public required long ProviderId { get; set; }

    public ExternalServiceProvider? Provider { get; set; }

    public required int Year { get; set; }

    public required int Month { get; set; }

    /// <summary>0 = balde mensual (cuota mensual). 1..31 = día del mes (cuota diaria, hora del Pacífico).</summary>
    public int Day { get; set; }

    public int UsedUnits { get; set; }
}
