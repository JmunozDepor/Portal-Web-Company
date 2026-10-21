using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;

namespace Modulo.Despachos.Servicios;

/// <summary>Viaje a costear (simulador, y más adelante manifiestos). LocalDepartureTime es hora Chile.</summary>
public sealed record SimulationInput(
    long CarrierId,
    long? ShiftId,
    long VehicleId,
    DateOnly DispatchDate,
    TimeOnly LocalDepartureTime,
    IReadOnlyList<long> CommuneIds,
    int Documents,
    int Units,
    int Packages,
    decimal? Km,
    IReadOnlyDictionary<long, int>? ManualQuantities = null);

/// <summary>
/// Tarifario persistido (valores base y recargos) y su evaluación. Todo se acota a la compañía
/// activa. Save* inserta si Id == 0, si no actualiza; las violaciones de reglas lanzan
/// InvalidOperationException con un mensaje apto para el usuario.
/// </summary>
public interface ITariffService
{
    Task<IReadOnlyList<RateCard>> ListRateCardsAsync();
    Task<long> SaveRateCardAsync(RateCard card);
    Task<DeleteResult> DeleteRateCardAsync(long id);

    Task<IReadOnlyList<RateSurcharge>> ListSurchargesAsync();
    Task<long> SaveSurchargeAsync(RateSurcharge surcharge);
    Task<DeleteResult> DeleteSurchargeAsync(long id);

    /// <summary>Tarifas y recargos activos (la vigencia por fecha la evalúa el motor).</summary>
    Task<(IReadOnlyList<RateCard>, IReadOnlyList<RateSurcharge>)> LoadActiveAsync();

    /// <summary>
    /// Arma el viaje para el motor: zona (única) y región de cada comuna, tipo y capacidad del
    /// vehículo (la propia o la de su tipo) y si la fecha de despacho es feriado.
    /// </summary>
    Task<TripCostInput> BuildTripAsync(SimulationInput input);

    Task<CostResult> SimulateAsync(SimulationInput input);
}
