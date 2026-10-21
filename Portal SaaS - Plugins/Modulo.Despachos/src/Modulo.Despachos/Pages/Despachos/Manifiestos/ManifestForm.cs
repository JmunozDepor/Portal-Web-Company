using System.Globalization;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Pages.Despachos.Manifiestos;

/// <summary>
/// Maestros activos que alimentan los selects de cabecera / cambio de vehículo, con los
/// nombres para mostrar (también de registros inactivos ya usados por un manifiesto).
/// </summary>
public sealed class ManifestLookups
{
    public IReadOnlyList<Carrier> Carriers { get; private init; } = Array.Empty<Carrier>();
    public IReadOnlyList<Vehicle> Vehicles { get; private init; } = Array.Empty<Vehicle>();
    public IReadOnlyList<Driver> Drivers { get; private init; } = Array.Empty<Driver>();
    public IReadOnlyList<Origin> Origins { get; private init; } = Array.Empty<Origin>();
    public IReadOnlyList<Shift> Shifts { get; private init; } = Array.Empty<Shift>();

    public IEnumerable<Vehicle> Tractors => Vehicles.Where(v => !v.IsTrailer && v.IsActive);
    public IEnumerable<Vehicle> Trailers => Vehicles.Where(v => v.IsTrailer && v.IsActive);

    public static async Task<ManifestLookups> LoadAsync(IMasterDataService data) => new()
    {
        Carriers = await data.ListCarriersAsync(includeInactive: true),
        Vehicles = await data.ListVehiclesAsync(includeInactive: true),
        Drivers = await data.ListDriversAsync(includeInactive: true),
        Origins = await data.ListOriginsAsync(includeInactive: true),
        Shifts = await data.ListShiftsAsync(includeInactive: true),
    };

    public string CarrierName(long? id) => Carriers.FirstOrDefault(x => x.Id == id)?.Name ?? "—";
    public string VehiclePlate(long? id) => Vehicles.FirstOrDefault(x => x.Id == id)?.Plate ?? "—";
    public string DriverName(long? id) => Drivers.FirstOrDefault(x => x.Id == id)?.FullName ?? "—";
    public string OriginName(long? id) => Origins.FirstOrDefault(x => x.Id == id)?.Name ?? "—";
    public string ShiftName(long? id) => Shifts.FirstOrDefault(x => x.Id == id)?.Name ?? "—";
}

/// <summary>
/// Formulario de cabecera de manifiesto. Fechas y horas llegan como texto ISO y los enteros
/// opcionales como int?; se convierten en cultura invariante (el Host fija es-CL).
/// </summary>
public sealed class ManifestForm
{
    public string? DispatchDate { get; set; }
    public long? ShiftId { get; set; }
    public long OriginId { get; set; }
    public long CarrierId { get; set; }
    public long VehicleId { get; set; }
    public long? TrailerId { get; set; }
    public long DriverId { get; set; }
    public string? EstimatedArrivalDate { get; set; }
    public string? EstimatedArrivalTime { get; set; }
    public string? PlannedDepartureTime { get; set; }
    public string? ServiceOrder { get; set; }
    public string? Remarks { get; set; }
    public int? OdometerStart { get; set; }

    public static ManifestForm From(Manifest m) => new()
    {
        DispatchDate = m.DispatchDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ShiftId = m.ShiftId, OriginId = m.OriginId, CarrierId = m.CarrierId, VehicleId = m.VehicleId,
        TrailerId = m.TrailerId, DriverId = m.DriverId,
        EstimatedArrivalDate = m.EstimatedArrivalDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        EstimatedArrivalTime = m.EstimatedArrivalTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
        PlannedDepartureTime = m.PlannedDepartureTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
        ServiceOrder = m.ServiceOrder, Remarks = m.Remarks, OdometerStart = m.OdometerStart,
    };

    public ManifestHeaderInput ToInput()
    {
        var date = ParseDate(DispatchDate, "La fecha de despacho") ?? throw new InvalidOperationException("Indique la fecha de despacho.");
        return new ManifestHeaderInput(
            date, ShiftId, OriginId, CarrierId, VehicleId, TrailerId, DriverId,
            ParseDate(EstimatedArrivalDate, "La fecha estimada de llegada"),
            ParseTime(EstimatedArrivalTime, "La hora estimada de llegada"),
            ParseTime(PlannedDepartureTime, "La hora de salida planificada"),
            NullIfBlank(ServiceOrder), NullIfBlank(Remarks), OdometerStart);
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateOnly? ParseDate(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var v))
            throw new InvalidOperationException($"{label} no es válida.");
        return v;
    }

    private static TimeOnly? ParseTime(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!TimeOnly.TryParseExact(text.Trim(), new[] { "HH:mm", "HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v))
            throw new InvalidOperationException($"{label} no es válida.");
        return v;
    }
}

/// <summary>Modelo del parcial _CabeceraCamposDespachos: valores del formulario, maestros y prefijo de los campos (Input / Header).</summary>
public sealed record ManifestFormView(ManifestForm Form, ManifestLookups Lookups, string Prefix);
