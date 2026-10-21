using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;

namespace Modulo.Despachos.Tests.Dominio.Tarifas;

internal static class TarifasTestData
{
    public static readonly DateOnly Fecha = new(2026, 11, 10); // martes

    public static readonly TripLocality Rancagua = new(1, 10, "06", "Rancagua");
    public static readonly TripLocality SantiagoCentro = new(2, 20, "13", "Santiago Centro");

    public static TripCostInput Trip(
        TimeOnly? salida = null, IReadOnlyList<TripLocality>? loc = null, long carrier = 1,
        decimal? m3 = null, decimal? km = null, int docs = 5, int packages = 10,
        long? shift = null, long? vehicleType = null, bool holiday = false, DateOnly? fecha = null) =>
        new(carrier, shift, vehicleType, m3, fecha ?? Fecha, salida ?? new TimeOnly(10, 0), holiday, km,
            docs, docs, packages, loc ?? new[] { Rancagua });

    public static RateCard Card(long id, decimal amount, string method = "fixed_per_trip",
        long? commune = null, long? zone = null, string? region = null, long? carrier = null,
        int priority = 0, decimal? min = null) =>
        new()
        {
            Id = id, Amount = amount, CalculationMethod = method, CommuneId = commune, ZoneId = zone,
            RegionCode = region, CarrierId = carrier, Priority = priority, MinAmount = min,
            ValidFrom = new DateOnly(2026, 1, 1)
        };

    public static RateSurcharge Surcharge(long id, string name, string condType, string condJson,
        string effect, decimal value, long? carrier = null, int priority = 0) =>
        new()
        {
            Id = id, Name = name, ConditionType = condType, ConditionParametersJson = condJson,
            EffectType = effect, Value = value, CarrierId = carrier, Priority = priority,
            ValidFrom = new DateOnly(2026, 1, 1)
        };

    public static CostEngine Engine() => new(
        new IRateCalculator[]
        {
            new FixedPerTripCalculator(), new PerM3Calculator(), new PerKmCalculator(),
            new PerPackageCalculator(), new PerDocumentCalculator(), new NoChargeCalculator()
        },
        new ISurchargeCondition[]
        {
            new DistinctZonesOverCondition(), new DistinctCommunesOverCondition(), new DepartureTimeFromCondition(),
            new ShiftIsCondition(), new WeekdayInCondition(), new IsHolidayCondition(),
            new DocumentsOverCondition(), new PackagesOverCondition(), new VehicleTypeIsCondition()
        });
}
