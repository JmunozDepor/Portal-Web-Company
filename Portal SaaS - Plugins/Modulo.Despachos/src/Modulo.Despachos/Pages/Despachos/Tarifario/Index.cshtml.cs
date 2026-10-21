using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;
using Modulo.Despachos.Pages.Despachos.Configuracion;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.Tarifario;

/// <summary>
/// Tarifario: valores base, recargos y simulador. Menú "tarifario" (permisos: Despachos.tarifario):
/// VIEW = ver las tres pestañas y simular; CREATE/EDIT/DELETE sobre valores base y recargos.
/// Los parámetros de las condiciones se arman desde campos tipados (nunca JSON a mano) y se
/// validan en TariffService con las mismas clases que usa el motor.
/// </summary>
public sealed class IndexModel : MaestroPageModelBase
{
    public static readonly (string Code, string Label)[] Methods =
    {
        ("fixed_per_trip", "Monto fijo por viaje"),
        ("per_m3", "Por m³ de capacidad del camión"),
        ("per_km", "Por kilómetro"),
        ("per_package", "Por bulto"),
        ("per_document", "Por documento"),
        ("no_charge", "Sin cobro (retiro)"),
    };

    public static readonly (string Code, string Label)[] ConditionTypes =
    {
        ("distinct_zones_over", "Zonas distintas sobre N"),
        ("distinct_communes_over", "Comunas distintas sobre N"),
        ("documents_over", "Documentos sobre N"),
        ("packages_over", "Bultos sobre N"),
        ("departure_time_from", "Salida desde una hora"),
        ("shift_is", "Turno igual a"),
        ("vehicle_type_is", "Tipo de vehículo igual a"),
        ("weekday_in", "Día de la semana"),
        ("is_holiday", "Despacho en feriado"),
        ("manual_quantity", "Cantidad ingresada en el manifiesto (adicional)"),
    };

    public static readonly (string Code, string Label)[] Effects =
    {
        ("fixed_amount", "Suma un monto fijo ($)"),
        ("percent_of_base", "Suma un % del valor base"),
        ("fixed_per_extra_unit", "$ por cada unidad (sobre el umbral, o cantidad ingresada)"),
    };

    public static readonly (int Day, string Label)[] Weekdays =
    {
        (1, "Lunes"), (2, "Martes"), (3, "Miércoles"), (4, "Jueves"), (5, "Viernes"), (6, "Sábado"), (0, "Domingo"),
    };

    private static readonly CultureInfo Clp = CultureInfo.GetCultureInfo("es-CL");

    private readonly ITariffService _tariff;
    private readonly IMasterDataService _data;

    public IndexModel(ITariffService tariff, IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _tariff = tariff;
        _data = data;
    }

    protected override string MenuCode => "tarifario";

    public IReadOnlyList<RateCard> Cards { get; private set; } = Array.Empty<RateCard>();
    public IReadOnlyList<RateSurcharge> Surcharges { get; private set; } = Array.Empty<RateSurcharge>();
    public IReadOnlyList<Carrier> Carriers { get; private set; } = Array.Empty<Carrier>();
    public IReadOnlyList<Shift> Shifts { get; private set; } = Array.Empty<Shift>();
    public IReadOnlyList<Zone> Zones { get; private set; } = Array.Empty<Zone>();
    public IReadOnlyList<Commune> Communes { get; private set; } = Array.Empty<Commune>();
    public IReadOnlyList<VehicleType> VehicleTypes { get; private set; } = Array.Empty<VehicleType>();
    public IReadOnlyList<Vehicle> Vehicles { get; private set; } = Array.Empty<Vehicle>();

    /// <summary>Regiones distintas del catálogo de comunas: el código nunca se escribe a mano.</summary>
    public IReadOnlyList<(string Code, string Name)> Regions { get; private set; } = Array.Empty<(string, string)>();

    /// <summary>Pestaña visible: valores | recargos | simulador.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    public string ActiveTab => Tab is "recargos" or "simulador" ? Tab : "valores";

    public CostResult? Result { get; private set; }
    public string? SimError { get; private set; }

    [BindProperty]
    public CardInput Card { get; set; } = new();

    [BindProperty]
    public SurchargeInput Surcharge { get; set; } = new();

    [BindProperty]
    public SimInput Sim { get; set; } = new();

    // ---------------------------------------------------------------- Formato para la vista

    /// <summary>Montos en pesos: sin decimales (salvo que la tarifa los tenga, ej. $/km).</summary>
    public static string Money(decimal v) => v.ToString("#,##0.##", Clp);

    public static string Inv(decimal? v) => v?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";

    public static string MethodLabel(string code) => Methods.FirstOrDefault(m => m.Code == code).Label ?? code;
    public static string ConditionLabel(string code) => ConditionTypes.FirstOrDefault(m => m.Code == code).Label ?? code;
    public static string EffectLabel(string code) => Effects.FirstOrDefault(m => m.Code == code).Label ?? code;

    public string CarrierName(long? id) => id is null ? "Todos" : Carriers.FirstOrDefault(c => c.Id == id)?.Name ?? $"#{id}";
    public string ShiftName(long? id) => id is null ? "Todos" : Shifts.FirstOrDefault(c => c.Id == id)?.Name ?? $"#{id}";
    public string VehicleTypeName(long? id) => id is null ? "Todos" : VehicleTypes.FirstOrDefault(c => c.Id == id)?.Name ?? $"#{id}";

    /// <summary>Destino de la tarifa: comuna, zona o región (la condición geográfica más específica).</summary>
    public string Geography(RateCard c)
    {
        if (c.CommuneId != null) return "Comuna " + (Communes.FirstOrDefault(x => x.Id == c.CommuneId)?.Name ?? $"#{c.CommuneId}");
        if (c.ZoneId != null) return "Zona " + (Zones.FirstOrDefault(x => x.Id == c.ZoneId)?.Name ?? $"#{c.ZoneId}");
        if (c.RegionCode != null) return "Región " + (Regions.FirstOrDefault(r => r.Code == c.RegionCode).Name ?? c.RegionCode);
        return "Todo el país";
    }

    public string M3Range(RateCard c) =>
        c.M3From == null && c.M3To == null ? "Cualquiera"
        : $"{(c.M3From == null ? "0" : Inv(c.M3From))} a {(c.M3To == null ? "sin tope" : Inv(c.M3To))}";

    public string Validity(DateOnly from, DateOnly? to) =>
        from.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) + " → " +
        (to?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "sin término");

    public string EffectText(RateSurcharge s) => s.EffectType switch
    {
        "percent_of_base" => $"+{Inv(s.Value)}% del base",
        "fixed_per_extra_unit" => $"+${Money(s.Value)} por unidad extra",
        _ => $"+${Money(s.Value)}",
    };

    /// <summary>Resumen legible de la condición, leyendo los mismos parámetros que el motor.</summary>
    public string ConditionText(RateSurcharge s)
    {
        var f = ReadConditionFields(s);
        return s.ConditionType switch
        {
            "distinct_zones_over" or "distinct_communes_over" or "documents_over" or "packages_over"
                => $"{ConditionLabel(s.ConditionType).Replace(" N", "")} {f.Threshold}",
            "departure_time_from" => $"Salida desde las {f.Time}",
            "shift_is" => "Turno " + ShiftName(f.ShiftId),
            "vehicle_type_is" => "Tipo " + VehicleTypeName(f.VehicleTypeId),
            "weekday_in" => "Días: " + string.Join(", ", Weekdays.Where(w => f.Days.Contains(w.Day.ToString())).Select(w => w.Label)),
            "is_holiday" => "Despacho en feriado",
            "manual_quantity" => "Cantidad ingresada en el manifiesto",
            _ => s.ConditionType,
        };
    }

    public object CardRow(RateCard c) => new
    {
        c.Id, c.CarrierId, c.ShiftId, c.RegionCode, c.ZoneId, c.CommuneId, c.VehicleTypeId,
        c.M3From, c.M3To, c.ValidFrom, c.ValidTo, c.CalculationMethod, c.Amount, c.MinAmount, c.Priority, c.IsActive,
    };

    /// <summary>Fila del recargo con los parámetros de la condición expandidos a campos tipados.</summary>
    public object SurchargeRow(RateSurcharge s)
    {
        var f = ReadConditionFields(s);
        return new
        {
            s.Id, s.CarrierId, s.Name, s.ValidFrom, s.ValidTo, s.Priority, s.IsActive, s.ConditionType,
            CondThreshold = f.Threshold, CondTime = f.Time, CondShiftId = f.ShiftId, CondVehicleTypeId = f.VehicleTypeId,
            CondDays = f.Days, s.EffectType, s.Value,
        };
    }

    private sealed record CondFields(string Threshold, string Time, long? ShiftId, long? VehicleTypeId, List<string> Days);

    private static CondFields ReadConditionFields(RateSurcharge s)
    {
        string threshold = "", time = "";
        long? shift = null, vtype = null;
        var days = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(s.ConditionParametersJson) ? "{}" : s.ConditionParametersJson);
            var r = doc.RootElement;
            if (r.TryGetProperty("threshold", out var t)) threshold = t.GetInt32().ToString(CultureInfo.InvariantCulture);
            if (r.TryGetProperty("time", out var tm)) time = tm.GetString() ?? "";
            if (r.TryGetProperty("shiftId", out var sh)) shift = sh.GetInt64();
            if (r.TryGetProperty("vehicleTypeId", out var vt)) vtype = vt.GetInt64();
            if (r.TryGetProperty("days", out var d))
                days = d.EnumerateArray().Select(x => x.GetInt32().ToString(CultureInfo.InvariantCulture)).ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Parámetros ilegibles (datos antiguos): se muestran vacíos y el motor avisa al calcular.
        }
        return new CondFields(threshold, time, shift, vtype, days);
    }

    // ---------------------------------------------------------------- Handlers

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        await LoadDataAsync();
        Sim = DefaultSim();
    });

    public Task<IActionResult> OnPostSaveCardAsync() => SaveCoreAsync(Card.Id, async () =>
    {
        var existing = Card.Id == 0 ? null : (await _tariff.ListRateCardsAsync()).FirstOrDefault(x => x.Id == Card.Id);
        await _tariff.SaveRateCardAsync(new RateCard
        {
            Id = Card.Id,
            CarrierId = Card.CarrierId,
            ShiftId = Card.ShiftId,
            RegionCode = NullIfBlank(Card.RegionCode),
            ZoneId = Card.ZoneId,
            CommuneId = Card.CommuneId,
            VehicleTypeId = Card.VehicleTypeId,
            M3From = ParseDecimal(Card.M3From, "El m³ desde"),
            M3To = ParseDecimal(Card.M3To, "El m³ hasta"),
            ValidFrom = ParseDate(Card.ValidFrom, "El inicio de vigencia") ?? default,
            ValidTo = ParseDate(Card.ValidTo, "El fin de vigencia"),
            CalculationMethod = Card.CalculationMethod ?? "",
            Amount = ParseDecimal(Card.Amount, "El monto") ?? 0m,
            MinAmount = ParseDecimal(Card.MinAmount, "El monto mínimo"),
            Priority = Card.Priority,
            IsActive = Card.IsActive,
            ParametersJson = existing?.ParametersJson,
        });
    }, Card.Id == 0 ? "Valor base creado." : "Valor base actualizado.", "valores");

    public Task<IActionResult> OnPostDeleteCardAsync(long id) =>
        DeleteCoreAsync(() => _tariff.DeleteRateCardAsync(id), "Valor base eliminado.", "valores");

    public Task<IActionResult> OnPostSaveSurchargeAsync() => SaveCoreAsync(Surcharge.Id, async () =>
        await _tariff.SaveSurchargeAsync(new RateSurcharge
        {
            Id = Surcharge.Id,
            CarrierId = Surcharge.CarrierId,
            Name = Surcharge.Name ?? "",
            ValidFrom = ParseDate(Surcharge.ValidFrom, "El inicio de vigencia") ?? default,
            ValidTo = ParseDate(Surcharge.ValidTo, "El fin de vigencia"),
            Priority = Surcharge.Priority,
            IsActive = Surcharge.IsActive,
            ConditionType = Surcharge.ConditionType ?? "",
            ConditionParametersJson = BuildConditionJson(Surcharge),
            EffectType = Surcharge.EffectType ?? "",
            Value = ParseDecimal(Surcharge.Value, "El valor") ?? 0m,
        }), Surcharge.Id == 0 ? "Recargo creado." : "Recargo actualizado.", "recargos");

    public Task<IActionResult> OnPostDeleteSurchargeAsync(long id) =>
        DeleteCoreAsync(() => _tariff.DeleteSurchargeAsync(id), "Recargo eliminado.", "recargos");

    /// <summary>Simular solo exige VIEW; no escribe nada.</summary>
    public async Task<IActionResult> OnPostSimulateAsync()
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        await LoadPermissionsAsync();
        Tab = "simulador";
        await LoadDataAsync();
        try
        {
            if (Sim.CarrierId is null) throw new InvalidOperationException("Seleccione el transportista.");
            if (Sim.VehicleId is null) throw new InvalidOperationException("Seleccione el vehículo.");
            var date = ParseDate(Sim.DispatchDate, "La fecha de despacho")
                ?? throw new InvalidOperationException("Indique la fecha de despacho.");
            if (!TimeOnly.TryParseExact(Sim.Time?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                throw new InvalidOperationException("La hora de salida debe tener formato HH:mm.");
            if (Sim.CommuneIds.Count == 0) throw new InvalidOperationException("Seleccione al menos una comuna destino.");

            Result = await _tariff.SimulateAsync(new SimulationInput(
                Sim.CarrierId.Value, Sim.ShiftId, Sim.VehicleId.Value, date, time, Sim.CommuneIds,
                Sim.Documents, Sim.Units, Sim.Packages, ParseDecimal(Sim.Km, "Los km")));
        }
        catch (Exception ex)
        {
            SimError = GetErrorMessage(ex);
        }
        return Page();
    }

    // ---------------------------------------------------------------- Helpers

    private async Task LoadDataAsync()
    {
        Cards = await _tariff.ListRateCardsAsync();
        Surcharges = await _tariff.ListSurchargesAsync();
        Carriers = await _data.ListCarriersAsync(includeInactive: true);
        Shifts = await _data.ListShiftsAsync(includeInactive: true);
        Zones = await _data.ListZonesAsync(includeInactive: true);
        Communes = await _data.ListCommunesAsync();
        VehicleTypes = await _data.ListVehicleTypesAsync(includeInactive: true);
        Vehicles = (await _data.ListVehiclesAsync(includeInactive: false)).Where(v => !v.IsTrailer).ToList();
        Regions = Communes.GroupBy(c => c.RegionCode).OrderBy(g => g.Key)
            .Select(g => (g.Key, g.First().RegionName)).ToList();
    }

    private static SimInput DefaultSim() => new()
    {
        DispatchDate = DateOnly.FromDateTime(ChileClock.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Time = "10:00",
        Documents = 1,
        Units = 1,
        Packages = 1,
    };

    /// <summary>JSON de parámetros desde los campos tipados del tipo de condición elegido.</summary>
    private static string? BuildConditionJson(SurchargeInput i)
    {
        switch (i.ConditionType)
        {
            case "distinct_zones_over" or "distinct_communes_over" or "documents_over" or "packages_over":
                if (!int.TryParse(i.CondThreshold?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                    throw new InvalidOperationException("El umbral debe ser un número entero (0 o más).");
                return JsonSerializer.Serialize(new { threshold = n });
            case "departure_time_from":
                if (!TimeOnly.TryParseExact(i.CondTime?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                    throw new InvalidOperationException("La hora debe tener formato HH:mm.");
                return JsonSerializer.Serialize(new { time = t.ToString("HH:mm", CultureInfo.InvariantCulture) });
            case "shift_is":
                if (i.CondShiftId is null) throw new InvalidOperationException("Seleccione el turno.");
                return JsonSerializer.Serialize(new { shiftId = i.CondShiftId.Value });
            case "vehicle_type_is":
                if (i.CondVehicleTypeId is null) throw new InvalidOperationException("Seleccione el tipo de vehículo.");
                return JsonSerializer.Serialize(new { vehicleTypeId = i.CondVehicleTypeId.Value });
            case "weekday_in":
                if (i.CondDays.Count == 0) throw new InvalidOperationException("Seleccione al menos un día de la semana.");
                return JsonSerializer.Serialize(new { days = i.CondDays.Distinct().OrderBy(d => d).ToList() });
            default:
                return null; // is_holiday y manual_quantity no llevan parámetros; un tipo desconocido lo rechaza el servicio.
        }
    }

    // Los números van como texto: el Host fija es-CL y el binder malinterpretaría "12.5".
    public sealed class CardInput
    {
        public long Id { get; set; }
        public long? CarrierId { get; set; }
        public long? ShiftId { get; set; }
        public string? RegionCode { get; set; }
        public long? ZoneId { get; set; }
        public long? CommuneId { get; set; }
        public long? VehicleTypeId { get; set; }
        public string? M3From { get; set; }
        public string? M3To { get; set; }
        public string? ValidFrom { get; set; }
        public string? ValidTo { get; set; }
        public string? CalculationMethod { get; set; }
        public string? Amount { get; set; }
        public string? MinAmount { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class SurchargeInput
    {
        public long Id { get; set; }
        public long? CarrierId { get; set; }
        public string? Name { get; set; }
        public string? ValidFrom { get; set; }
        public string? ValidTo { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
        public string? ConditionType { get; set; }
        public string? CondThreshold { get; set; }
        public string? CondTime { get; set; }
        public long? CondShiftId { get; set; }
        public long? CondVehicleTypeId { get; set; }
        public List<int> CondDays { get; set; } = new();
        public string? EffectType { get; set; }
        public string? Value { get; set; }
    }

    public sealed class SimInput
    {
        public long? CarrierId { get; set; }
        public long? ShiftId { get; set; }
        public long? VehicleId { get; set; }
        public string? DispatchDate { get; set; }
        public string? Time { get; set; }
        public List<long> CommuneIds { get; set; } = new();
        public int Documents { get; set; }
        public int Units { get; set; }
        public int Packages { get; set; }
        public string? Km { get; set; }
    }
}
