using System.Globalization;
using System.Text.Json;

namespace Modulo.Despachos.Dominio.Tarifas;

/// <summary>Base de los "*_over": cumple si valor > umbral; ExtraUnits = valor - umbral.</summary>
public abstract class OverThresholdCondition : ISurchargeCondition
{
    public abstract string Type { get; }
    protected abstract string Label { get; }
    protected abstract int Value(TripCostInput trip);

    public SurchargeEvaluation Evaluate(JsonElement parameters, TripCostInput trip)
    {
        var threshold = parameters.GetProperty("threshold").GetInt32();
        if (threshold < 0) throw new InvalidOperationException("El umbral no puede ser negativo.");
        var value = Value(trip);
        var met = value > threshold;
        return new SurchargeEvaluation(met, met ? value - threshold : 0, $"{Label}: {value} (umbral {threshold})");
    }
}

public sealed class DistinctZonesOverCondition : OverThresholdCondition
{
    public override string Type => "distinct_zones_over";
    protected override string Label => "Zonas distintas";
    protected override int Value(TripCostInput t) =>
        t.Localities.Where(l => l.ZoneId != null).Select(l => l.ZoneId).Distinct().Count();
}

public sealed class DistinctCommunesOverCondition : OverThresholdCondition
{
    public override string Type => "distinct_communes_over";
    protected override string Label => "Comunas distintas";
    protected override int Value(TripCostInput t) => t.Localities.Select(l => l.CommuneId).Distinct().Count();
}

public sealed class DocumentsOverCondition : OverThresholdCondition
{
    public override string Type => "documents_over";
    protected override string Label => "Documentos";
    protected override int Value(TripCostInput t) => t.Documents;
}

public sealed class PackagesOverCondition : OverThresholdCondition
{
    public override string Type => "packages_over";
    protected override string Label => "Bultos";
    protected override int Value(TripCostInput t) => t.Packages;
}

public sealed class DepartureTimeFromCondition : ISurchargeCondition
{
    public string Type => "departure_time_from";
    public SurchargeEvaluation Evaluate(JsonElement parameters, TripCostInput trip)
    {
        var from = TimeOnly.ParseExact(parameters.GetProperty("time").GetString()!, "HH:mm", CultureInfo.InvariantCulture);
        var met = trip.LocalDepartureTime >= from;
        return new SurchargeEvaluation(met, 1,
            $"Salida a las {trip.LocalDepartureTime.ToString("HH:mm", CultureInfo.InvariantCulture)} (recargo desde las {from.ToString("HH:mm", CultureInfo.InvariantCulture)})");
    }
}

public sealed class ShiftIsCondition : ISurchargeCondition
{
    public string Type => "shift_is";
    public SurchargeEvaluation Evaluate(JsonElement parameters, TripCostInput trip)
    {
        var id = parameters.GetProperty("shiftId").GetInt64();
        return new SurchargeEvaluation(trip.ShiftId == id, 1, $"Turno del viaje igual al turno {id}");
    }
}

public sealed class VehicleTypeIsCondition : ISurchargeCondition
{
    public string Type => "vehicle_type_is";
    public SurchargeEvaluation Evaluate(JsonElement parameters, TripCostInput trip)
    {
        var id = parameters.GetProperty("vehicleTypeId").GetInt64();
        return new SurchargeEvaluation(trip.VehicleTypeId == id, 1, $"Tipo de vehículo igual al tipo {id}");
    }
}

/// <summary>days = DayOfWeek numérico (0 = domingo ... 6 = sábado) de la fecha de despacho.</summary>
public sealed class WeekdayInCondition : ISurchargeCondition
{
    public string Type => "weekday_in";
    public SurchargeEvaluation Evaluate(JsonElement parameters, TripCostInput trip)
    {
        var days = parameters.GetProperty("days").EnumerateArray().Select(d => d.GetInt32()).ToList();
        if (days.Count == 0 || days.Any(d => d < 0 || d > 6))
            throw new InvalidOperationException("Los días deben ser números entre 0 (domingo) y 6 (sábado).");
        var dow = (int)trip.DispatchDate.DayOfWeek;
        return new SurchargeEvaluation(days.Contains(dow), 1, $"Día de la semana {dow} (recargo en {string.Join(",", days)})");
    }
}

public sealed class IsHolidayCondition : ISurchargeCondition
{
    public string Type => "is_holiday";
    public SurchargeEvaluation Evaluate(JsonElement parameters, TripCostInput trip) =>
        new(trip.IsHoliday, 1, "Despacho en día feriado");
}

/// <summary>
/// Cantidad que logística ingresa en cada manifiesto (ayudante adicional, punto adicional...). No depende del viaje:
/// el motor la lee de TripCostInput.ManualQuantities por id del recargo, vía <see cref="ForQuantity"/>. Con el efecto
/// fixed_per_extra_unit cobra cantidad × valor; sin cantidad (0) no aplica. Sin parámetros.
/// </summary>
public sealed class ManualQuantityCondition : ISurchargeCondition
{
    public const string Code = "manual_quantity";
    public string Type => Code;

    public SurchargeEvaluation Evaluate(JsonElement parameters, TripCostInput trip) => ForQuantity(0);

    public static SurchargeEvaluation ForQuantity(int quantity) =>
        new(quantity > 0, Math.Max(quantity, 0), $"Cantidad ingresada: {quantity}");
}
