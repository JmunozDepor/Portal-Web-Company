namespace Modulo.Despachos.Dominio.Tarifas;

/// <summary>Destino (comuna) del viaje; solo selecciona la tarifa, los montos usan los totales del viaje.</summary>
public record TripLocality(long CommuneId, long? ZoneId, string RegionCode, string CommuneName);

/// <summary>Datos del viaje que alimentan el motor de costo. LocalDepartureTime es hora Chile.</summary>
public record TripCostInput(
    long CarrierId,
    long? ShiftId,
    long? VehicleTypeId,
    decimal? CapacityM3,
    DateOnly DispatchDate,
    TimeOnly LocalDepartureTime,
    bool IsHoliday,
    decimal? Km,
    int Documents,
    int Units,
    int Packages,
    IReadOnlyList<TripLocality> Localities);
