using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;
using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Servicios;

public sealed class TariffService : ITariffService
{
    private static readonly string[] EffectTypes = { "fixed_amount", "percent_of_base", "fixed_per_extra_unit" };

    private readonly DespachosDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly CostEngine _engine;
    private readonly Dictionary<string, ISurchargeCondition> _conditions;
    private readonly HashSet<string> _methods;

    public TariffService(
        DespachosDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user, CostEngine engine,
        IEnumerable<IRateCalculator> calculators, IEnumerable<ISurchargeCondition> conditions)
    {
        _db = db;
        _company = company;
        _user = user;
        _engine = engine;
        _conditions = conditions.ToDictionary(c => c.Type);
        _methods = calculators.Select(c => c.Method).ToHashSet();
    }

    private Guid CompanyId => _company.CompanyId;

    // ---------------------------------------------------------------- Valores base

    public async Task<IReadOnlyList<RateCard>> ListRateCardsAsync() =>
        await _db.RateCards.Where(r => r.CompanyId == CompanyId)
            .OrderBy(r => r.CarrierId).ThenBy(r => r.RegionCode).ThenBy(r => r.Id).ToListAsync();

    public async Task<long> SaveRateCardAsync(RateCard c)
    {
        if (!_methods.Contains(c.CalculationMethod ?? ""))
            throw new InvalidOperationException($"Modalidad de cálculo desconocida: '{c.CalculationMethod}'.");
        if (c.ValidFrom == default)
            throw new InvalidOperationException("La fecha de inicio de vigencia es obligatoria.");
        if (c.ValidTo != null && c.ValidTo < c.ValidFrom)
            throw new InvalidOperationException("La vigencia termina antes de comenzar.");
        if (c.M3From != null && c.M3To != null && c.M3From > c.M3To)
            throw new InvalidOperationException("El rango de m³ es inválido: el desde es mayor que el hasta.");
        if (c.M3From < 0 || c.M3To < 0)
            throw new InvalidOperationException("Los m³ no pueden ser negativos.");
        if (c.Amount < 0) throw new InvalidOperationException("El monto no puede ser negativo.");
        if (c.MinAmount < 0) throw new InvalidOperationException("El monto mínimo no puede ser negativo.");

        c.RegionCode = string.IsNullOrWhiteSpace(c.RegionCode) ? null : c.RegionCode.Trim();
        if (c.RegionCode != null
            && !await _db.Communes.AnyAsync(x => x.CompanyId == CompanyId && x.RegionCode == c.RegionCode))
            throw new InvalidOperationException($"La región '{c.RegionCode}' no existe en el catálogo de comunas.");

        if (string.IsNullOrWhiteSpace(c.ParametersJson))
        {
            c.ParametersJson = null;
        }
        else
        {
            try { using var _ = JsonDocument.Parse(c.ParametersJson); }
            catch (JsonException) { throw new InvalidOperationException("Los parámetros de la tarifa no son un JSON válido."); }
        }

        await EnsureExistsAsync<Carrier>(c.CarrierId, "El transportista");
        await EnsureExistsAsync<Shift>(c.ShiftId, "El turno");
        await EnsureExistsAsync<Zone>(c.ZoneId, "La zona");
        await EnsureExistsAsync<Commune>(c.CommuneId, "La comuna");
        await EnsureExistsAsync<VehicleType>(c.VehicleTypeId, "El tipo de vehículo");

        return await SaveAsync(c, _db.RateCards);
    }

    public Task<DeleteResult> DeleteRateCardAsync(long id) => DeleteAsync(_db.RateCards, id, async () =>
        await _db.Manifests.AnyAsync(m => m.CompanyId == CompanyId && m.RateCardId == id)
        || await _db.ManifestCostLines.AnyAsync(l => l.CompanyId == CompanyId && l.RateCardId == id)
            ? "La tarifa está usada en manifiestos; desactívela en vez de eliminarla."
            : null);

    // ---------------------------------------------------------------- Recargos

    public async Task<IReadOnlyList<RateSurcharge>> ListSurchargesAsync() =>
        await _db.RateSurcharges.Where(r => r.CompanyId == CompanyId)
            .OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync();

    public async Task<long> SaveSurchargeAsync(RateSurcharge s)
    {
        s.Name = s.Name?.Trim() ?? "";
        if (s.Name.Length == 0) throw new InvalidOperationException("El nombre es obligatorio.");
        if (s.ValidFrom == default)
            throw new InvalidOperationException("La fecha de inicio de vigencia es obligatoria.");
        if (s.ValidTo != null && s.ValidTo < s.ValidFrom)
            throw new InvalidOperationException("La vigencia termina antes de comenzar.");
        if (!EffectTypes.Contains(s.EffectType))
            throw new InvalidOperationException($"Tipo de efecto desconocido: '{s.EffectType}'.");
        if (s.Value <= 0) throw new InvalidOperationException("El valor del recargo debe ser mayor que cero.");
        if (s.EffectType == "percent_of_base" && s.Value > 1000)
            throw new InvalidOperationException("El porcentaje del recargo no puede superar 1000%.");

        await ValidateConditionAsync(s);
        await EnsureExistsAsync<Carrier>(s.CarrierId, "El transportista");
        return await SaveAsync(s, _db.RateSurcharges);
    }

    /// <summary>
    /// Valida los parámetros con las mismas clases que usa el motor: se evalúa la condición contra un
    /// viaje neutro, de modo que cualquier parámetro que el motor no pueda leer se rechaza al guardar.
    /// </summary>
    private async Task ValidateConditionAsync(RateSurcharge s)
    {
        if (!_conditions.TryGetValue(s.ConditionType ?? "", out var condition))
            throw new InvalidOperationException($"Tipo de condición desconocido: '{s.ConditionType}'.");

        s.ConditionParametersJson = string.IsNullOrWhiteSpace(s.ConditionParametersJson) ? null : s.ConditionParametersJson;
        var neutral = new TripCostInput(0, null, null, null, s.ValidFrom, TimeOnly.MinValue, false, null, 0, 0, 0,
            Array.Empty<TripLocality>());

        long? shiftId = null, vehicleTypeId = null;
        try
        {
            using var doc = JsonDocument.Parse(s.ConditionParametersJson ?? "{}");
            condition.Evaluate(doc.RootElement, neutral);
            if (s.ConditionType == "shift_is") shiftId = doc.RootElement.GetProperty("shiftId").GetInt64();
            if (s.ConditionType == "vehicle_type_is") vehicleTypeId = doc.RootElement.GetProperty("vehicleTypeId").GetInt64();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException
                                       or FormatException or ArgumentException or OverflowException)
        {
            throw new InvalidOperationException(
                $"Parámetros inválidos para la condición '{s.ConditionType}': {ex.Message}", ex);
        }

        // Las condiciones por id apuntan a maestros de la compañía.
        await EnsureExistsAsync<Shift>(shiftId, "El turno");
        await EnsureExistsAsync<VehicleType>(vehicleTypeId, "El tipo de vehículo");
    }

    public Task<DeleteResult> DeleteSurchargeAsync(long id) => DeleteAsync(_db.RateSurcharges, id, async () =>
        await _db.ManifestCostLines.AnyAsync(l => l.CompanyId == CompanyId && l.RateSurchargeId == id)
        || await _db.ManifestExtraCharges.AnyAsync(x => x.CompanyId == CompanyId && x.RateSurchargeId == id)
            ? "El recargo está usado en manifiestos; desactívelo en vez de eliminarlo."
            : null);

    // ---------------------------------------------------------------- Evaluación

    public async Task<(IReadOnlyList<RateCard>, IReadOnlyList<RateSurcharge>)> LoadActiveAsync()
    {
        var cards = await _db.RateCards.Where(r => r.CompanyId == CompanyId && r.IsActive).ToListAsync();
        var surcharges = await _db.RateSurcharges.Where(r => r.CompanyId == CompanyId && r.IsActive).ToListAsync();
        return (cards, surcharges);
    }

    public async Task<TripCostInput> BuildTripAsync(SimulationInput input)
    {
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == input.VehicleId && v.CompanyId == CompanyId)
            ?? throw new InvalidOperationException("El vehículo seleccionado no existe.");
        VehicleType? type = null;
        if (vehicle.VehicleTypeId != null)
            type = await _db.VehicleTypes.FirstOrDefaultAsync(t => t.Id == vehicle.VehicleTypeId && t.CompanyId == CompanyId);

        var communeIds = input.CommuneIds.Distinct().ToList();
        var communes = await _db.Communes
            .Where(c => c.CompanyId == CompanyId && communeIds.Contains(c.Id)).ToListAsync();
        if (communes.Count != communeIds.Count)
            throw new InvalidOperationException("Alguna comuna del viaje no existe en el catálogo.");

        // Una comuna pertenece como máximo a una zona (lo garantiza SaveZoneAsync); solo cuentan zonas activas.
        var links = await (from zc in _db.ZoneCommunes
                           join z in _db.Zones on zc.ZoneId equals z.Id
                           where zc.CompanyId == CompanyId && z.IsActive && communeIds.Contains(zc.CommuneId)
                           select new { zc.CommuneId, zc.ZoneId }).ToListAsync();
        var zoneOf = links.GroupBy(l => l.CommuneId).ToDictionary(g => g.Key, g => (long?)g.First().ZoneId);

        var localities = communeIds.Select(id =>
        {
            var c = communes.First(x => x.Id == id);
            return new TripLocality(c.Id, zoneOf.GetValueOrDefault(c.Id), c.RegionCode, c.Name);
        }).ToList();

        var isHoliday = await _db.Holidays.AnyAsync(h => h.CompanyId == CompanyId && h.Date == input.DispatchDate);

        return new TripCostInput(
            input.CarrierId, input.ShiftId, vehicle.VehicleTypeId, vehicle.CapacityM3 ?? type?.CapacityM3,
            input.DispatchDate, input.LocalDepartureTime, isHoliday, input.Km,
            input.Documents, input.Units, input.Packages, localities, input.ManualQuantities);
    }

    public async Task<CostResult> SimulateAsync(SimulationInput input)
    {
        var trip = await BuildTripAsync(input);
        var (cards, surcharges) = await LoadActiveAsync();
        return _engine.Calculate(trip, cards, surcharges);
    }

    // ---------------------------------------------------------------- Helpers

    private async Task EnsureExistsAsync<T>(long? id, string label) where T : AuditedEntity
    {
        if (id is null) return;
        if (!await _db.Set<T>().AnyAsync(x => x.Id == id && x.CompanyId == CompanyId))
            throw new InvalidOperationException($"{label} seleccionado no existe.");
    }

    private async Task<long> SaveAsync<T>(T incoming, DbSet<T> set) where T : AuditedEntity
    {
        var now = DateTime.UtcNow;
        if (incoming.Id == 0)
        {
            incoming.CompanyId = CompanyId;
            incoming.CreatedAt = now;
            incoming.CreatedBy = _user.UserId;
            set.Add(incoming);
            await _db.SaveChangesAsync();
            return incoming.Id;
        }

        var existing = await set.FirstOrDefaultAsync(x => x.Id == incoming.Id && x.CompanyId == CompanyId)
            ?? throw new InvalidOperationException("El registro no existe.");
        var (companyId, createdAt, createdBy) = (existing.CompanyId, existing.CreatedAt, existing.CreatedBy);
        _db.Entry(existing).CurrentValues.SetValues(incoming);
        existing.CompanyId = companyId;
        existing.CreatedAt = createdAt;
        existing.CreatedBy = createdBy;
        existing.UpdatedAt = now;
        existing.UpdatedBy = _user.UserId;
        await _db.SaveChangesAsync();
        return existing.Id;
    }

    private async Task<DeleteResult> DeleteAsync<T>(DbSet<T> set, long id, Func<Task<string?>> blockReason) where T : AuditedEntity
    {
        var entity = await set.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        if (entity is null) return new DeleteResult(false, "El registro no existe.");
        var reason = await blockReason();
        if (reason is not null) return new DeleteResult(false, reason);
        set.Remove(entity);
        await _db.SaveChangesAsync();
        return new DeleteResult(true, null);
    }
}
