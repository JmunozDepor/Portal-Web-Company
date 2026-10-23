using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Servicios;

/// <summary>
/// Implementación de los mantenedores. Cada consulta filtra por la compañía activa y cada
/// inserción la fija; editar un registro de otra compañía falla como "no existe".
/// Transportistas, vehículos, choferes, orígenes y turnos usados en manifiestos no se eliminan
/// (son historial): se desactivan.
/// </summary>
public sealed class MasterDataService : IMasterDataService
{
    private readonly DespachosDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;

    public MasterDataService(DespachosDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user)
    {
        _db = db;
        _company = company;
        _user = user;
    }

    private Guid CompanyId => _company.CompanyId;

    // ---------------------------------------------------------------- Ajustes de la compañía

    public async Task<string?> GetIssuerTaxIdAsync() =>
        (await _db.Settings.FirstOrDefaultAsync(x => x.CompanyId == CompanyId))?.IssuerTaxId;

    public async Task SaveIssuerTaxIdAsync(string? rut)
    {
        var normalized = string.IsNullOrWhiteSpace(rut) ? null : NormalizeRut(rut);
        var row = await _db.Settings.FirstOrDefaultAsync(x => x.CompanyId == CompanyId);
        var now = DateTime.UtcNow;
        if (row is null)
        {
            _db.Settings.Add(new DespachosSetting
            {
                CompanyId = CompanyId, IssuerTaxId = normalized, CreatedAt = now, CreatedBy = _user.UserId,
            });
        }
        else
        {
            row.IssuerTaxId = normalized;
            row.UpdatedAt = now;
            row.UpdatedBy = _user.UserId;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<DespachosSetting?> GetSettingsAsync() =>
        await _db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == CompanyId);

    public async Task SaveGuideSettingsAsync(bool guidesEnabled, int transferIndicator, int? dispatchType, string lineGrouping = GuideLineGroupings.Sku)
    {
        if (!GuideLineGroupings.All.Contains(lineGrouping))
            throw new InvalidOperationException($"Resumen de líneas inválido: '{lineGrouping}'.");
        if (transferIndicator is < 1 or > 9)
            throw new InvalidOperationException("El indicador de traslado debe estar entre 1 y 9.");
        if (dispatchType is not null and (< 1 or > 3))
            throw new InvalidOperationException("El tipo de despacho debe ser 1, 2 o 3.");
        var row = await _db.Settings.FirstOrDefaultAsync(x => x.CompanyId == CompanyId);
        var now = DateTime.UtcNow;
        if (row is null)
        {
            row = new DespachosSetting { CompanyId = CompanyId, CreatedAt = now, CreatedBy = _user.UserId };
            _db.Settings.Add(row);
        }
        else
        {
            row.UpdatedAt = now;
            row.UpdatedBy = _user.UserId;
        }
        row.GuidesEnabled = guidesEnabled;
        row.TransferIndicator = transferIndicator;
        row.DispatchType = dispatchType;
        row.GuideLineGrouping = lineGrouping;
        await _db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- Transportistas

    public async Task<IReadOnlyList<Carrier>> ListCarriersAsync(bool includeInactive = false) =>
        await Scoped(_db.Carriers, includeInactive, c => c.IsActive).OrderBy(c => c.Name).ToListAsync();

    public async Task<long> SaveCarrierAsync(Carrier e)
    {
        e.Code = Required(e.Code, "El código");
        e.Name = Required(e.Name, "El nombre");
        if (!CarrierKinds.All.Contains(e.Kind))
            throw new InvalidOperationException($"Tipo de transportista inválido: '{e.Kind}'.");

        if (string.IsNullOrWhiteSpace(e.TaxId))
        {
            // La Res.154 exige RUT del transportista salvo retiro por el cliente.
            if (e.Kind != CarrierKinds.Pickup)
                throw new InvalidOperationException("El RUT del transportista es obligatorio (solo el tipo Retiro puede ir sin RUT).");
            e.TaxId = null;
        }
        else
        {
            e.TaxId = NormalizeRut(e.TaxId);
        }

        return await SaveAsync(e, x => x.Code == e.Code, $"Ya existe un transportista con el código '{e.Code}'.");
    }

    public Task<DeleteResult> DeleteCarrierAsync(long id) => DeleteAsync(_db.Carriers, id, async () =>
    {
        if (await _db.Manifests.AnyAsync(m => m.CompanyId == CompanyId && m.CarrierId == id))
            return "El transportista se usó en manifiestos; desactívelo en vez de eliminarlo.";
        if (await _db.Vehicles.AnyAsync(v => v.CompanyId == CompanyId && v.CarrierId == id))
            return "El transportista tiene vehículos asociados; desactívelo en vez de eliminarlo.";
        if (await _db.Drivers.AnyAsync(d => d.CompanyId == CompanyId && d.CarrierId == id))
            return "El transportista tiene choferes asociados; desactívelo en vez de eliminarlo.";
        if (await _db.RateCards.AnyAsync(r => r.CompanyId == CompanyId && r.CarrierId == id)
            || await _db.RateSurcharges.AnyAsync(r => r.CompanyId == CompanyId && r.CarrierId == id))
            return "El transportista tiene tarifas o recargos en el tarifario; desactívelo en vez de eliminarlo.";
        return null;
    });

    // ---------------------------------------------------------------- Tipos de vehículo

    public async Task<IReadOnlyList<VehicleType>> ListVehicleTypesAsync(bool includeInactive = false) =>
        await Scoped(_db.VehicleTypes, includeInactive, t => t.IsActive).OrderBy(t => t.Name).ToListAsync();

    public async Task<long> SaveVehicleTypeAsync(VehicleType e)
    {
        e.Code = Required(e.Code, "El código");
        e.Name = Required(e.Name, "El nombre");
        return await SaveAsync(e, x => x.Code == e.Code, $"Ya existe un tipo de vehículo con el código '{e.Code}'.");
    }

    public Task<DeleteResult> DeleteVehicleTypeAsync(long id) => DeleteAsync(_db.VehicleTypes, id, async () =>
    {
        if (await _db.Vehicles.AnyAsync(v => v.CompanyId == CompanyId && v.VehicleTypeId == id))
            return "Hay vehículos de este tipo; desactívelo en vez de eliminarlo.";
        if (await _db.RateCards.AnyAsync(r => r.CompanyId == CompanyId && r.VehicleTypeId == id))
            return "Hay tarifas del tarifario para este tipo; desactívelo en vez de eliminarlo.";
        return null;
    });

    // ---------------------------------------------------------------- Vehículos

    public async Task<IReadOnlyList<Vehicle>> ListVehiclesAsync(bool includeInactive = false) =>
        await Scoped(_db.Vehicles, includeInactive, v => v.IsActive).OrderBy(v => v.Plate).ToListAsync();

    public async Task<long> SaveVehicleAsync(Vehicle e)
    {
        e.Plate = PlateNormalizer.Normalize(e.Plate ?? "");
        if (e.Plate.Length == 0) throw new InvalidOperationException("La patente es obligatoria.");
        await EnsureExistsAsync<VehicleType>(e.VehicleTypeId, "El tipo de vehículo");
        await EnsureExistsAsync<Carrier>(e.CarrierId, "El transportista");
        return await SaveAsync(e, x => x.Plate == e.Plate, $"La patente '{e.Plate}' ya existe.");
    }

    public Task<DeleteResult> DeleteVehicleAsync(long id) => DeleteAsync(_db.Vehicles, id, async () =>
        await _db.Manifests.AnyAsync(m => m.CompanyId == CompanyId && (m.VehicleId == id || m.TrailerId == id))
            ? "El vehículo se usó en manifiestos; desactívelo en vez de eliminarlo."
            : null);

    // ---------------------------------------------------------------- Choferes

    public async Task<IReadOnlyList<Driver>> ListDriversAsync(bool includeInactive = false) =>
        await Scoped(_db.Drivers, includeInactive, d => d.IsActive).OrderBy(d => d.FullName).ToListAsync();

    public async Task<long> SaveDriverAsync(Driver e)
    {
        e.FullName = Required(e.FullName, "El nombre");
        e.TaxId = NormalizeRut(e.TaxId);
        e.LicenseClass = string.IsNullOrWhiteSpace(e.LicenseClass) ? null : e.LicenseClass.Trim();
        await EnsureExistsAsync<Carrier>(e.CarrierId, "El transportista");
        return await SaveAsync(e, x => x.TaxId == e.TaxId, $"Ya existe un chofer con el RUT '{e.TaxId}'.");
    }

    public Task<DeleteResult> DeleteDriverAsync(long id) => DeleteAsync(_db.Drivers, id, async () =>
        await _db.Manifests.AnyAsync(m => m.CompanyId == CompanyId && m.DriverId == id)
            ? "El chofer se usó en manifiestos; desactívelo en vez de eliminarlo."
            : null);

    // ---------------------------------------------------------------- Turnos

    public async Task<IReadOnlyList<Shift>> ListShiftsAsync(bool includeInactive = false) =>
        await Scoped(_db.Shifts, includeInactive, s => s.IsActive).OrderBy(s => s.Name).ToListAsync();

    public async Task<long> SaveShiftAsync(Shift e)
    {
        e.Code = Required(e.Code, "El código");
        e.Name = Required(e.Name, "El nombre");
        return await SaveAsync(e, x => x.Code == e.Code, $"Ya existe un turno con el código '{e.Code}'.");
    }

    public Task<DeleteResult> DeleteShiftAsync(long id) => DeleteAsync(_db.Shifts, id, async () =>
    {
        if (await _db.RateCards.AnyAsync(r => r.CompanyId == CompanyId && r.ShiftId == id))
            return "Hay tarifas del tarifario para este turno; desactívelo en vez de eliminarlo.";
        if (await _db.Manifests.AnyAsync(m => m.CompanyId == CompanyId && m.ShiftId == id))
            return "El turno se usó en manifiestos; desactívelo en vez de eliminarlo.";
        return null;
    });

    // ---------------------------------------------------------------- Zonas

    public async Task<IReadOnlyList<Zone>> ListZonesAsync(bool includeInactive = false) =>
        await Scoped(_db.Zones, includeInactive, z => z.IsActive).Include(z => z.Communes).OrderBy(z => z.Name).ToListAsync();

    public async Task<long> SaveZoneAsync(Zone e)
    {
        e.Code = Required(e.Code, "El código");
        e.Name = Required(e.Name, "El nombre");

        // Comunas pedidas, sin repetidas; deben pertenecer al catálogo de la compañía.
        var communeIds = e.Communes.Select(c => c.CommuneId).Distinct().ToList();
        var valid = await _db.Communes.CountAsync(c => c.CompanyId == CompanyId && communeIds.Contains(c.Id));
        if (valid != communeIds.Count)
            throw new InvalidOperationException("Alguna comuna de la zona no existe en el catálogo.");

        // Una comuna pertenece como máximo a UNA zona por compañía (la zona de un viaje se deriva de ella).
        var taken = await _db.ZoneCommunes
            .Where(zc => zc.CompanyId == CompanyId && zc.ZoneId != e.Id && communeIds.Contains(zc.CommuneId))
            .ToListAsync();
        if (taken.Count > 0)
        {
            var first = taken[0];
            var communeName = (await _db.Communes.FirstAsync(c => c.Id == first.CommuneId)).Name;
            var zoneName = (await _db.Zones.FirstAsync(z => z.Id == first.ZoneId)).Name;
            throw new InvalidOperationException($"La comuna {communeName} ya pertenece a la zona {zoneName}.");
        }
        e.Communes = communeIds.Select(id => new ZoneCommune { CompanyId = CompanyId, CommuneId = id }).ToList();

        return await SaveAsync(e, x => x.Code == e.Code, $"Ya existe una zona con el código '{e.Code}'.",
            include: q => q.Include(z => z.Communes),
            syncChildren: (existing, incoming) =>
            {
                var wanted = incoming.Communes.Select(c => c.CommuneId).ToHashSet();
                foreach (var gone in existing.Communes.Where(c => !wanted.Contains(c.CommuneId)).ToList())
                {
                    existing.Communes.Remove(gone);
                    _db.ZoneCommunes.Remove(gone);
                }
                var have = existing.Communes.Select(c => c.CommuneId).ToHashSet();
                foreach (var id in wanted.Where(id => !have.Contains(id)))
                    existing.Communes.Add(new ZoneCommune { CompanyId = CompanyId, CommuneId = id });
            });
    }

    public async Task<DeleteResult> DeleteZoneAsync(long id)
    {
        var zone = await _db.Zones.Include(z => z.Communes)
            .FirstOrDefaultAsync(z => z.Id == id && z.CompanyId == CompanyId);
        if (zone is null) return new DeleteResult(false, "El registro no existe.");
        if (await _db.RateCards.AnyAsync(r => r.CompanyId == CompanyId && r.ZoneId == id))
            return new DeleteResult(false, "Hay tarifas del tarifario para esta zona; desactívela en vez de eliminarla.");

        // Las comunas de la zona son parte de la zona: se borran con ella.
        _db.ZoneCommunes.RemoveRange(zone.Communes);
        _db.Zones.Remove(zone);
        await _db.SaveChangesAsync();
        return new DeleteResult(true, null);
    }

    // ---------------------------------------------------------------- Orígenes

    public async Task<IReadOnlyList<Origin>> ListOriginsAsync(bool includeInactive = false) =>
        await Scoped(_db.Origins, includeInactive, o => o.IsActive).OrderBy(o => o.Name).ToListAsync();

    public async Task<long> SaveOriginAsync(Origin e)
    {
        e.Code = Required(e.Code, "El código");
        e.Name = Required(e.Name, "El nombre");
        await EnsureExistsAsync<Commune>(e.CommuneId, "La comuna");
        return await SaveAsync(e, x => x.Code == e.Code, $"Ya existe un origen con el código '{e.Code}'.");
    }

    public Task<DeleteResult> DeleteOriginAsync(long id) => DeleteAsync(_db.Origins, id, async () =>
        await _db.Manifests.AnyAsync(m => m.CompanyId == CompanyId && m.OriginId == id)
            ? "El origen se usó en manifiestos; desactívelo en vez de eliminarlo."
            : null);

    // ---------------------------------------------------------------- Motivos de rechazo

    public async Task<IReadOnlyList<RejectionReason>> ListRejectionReasonsAsync(bool includeInactive = false) =>
        await Scoped(_db.RejectionReasons, includeInactive, r => r.IsActive).OrderBy(r => r.Name).ToListAsync();

    public async Task<long> SaveRejectionReasonAsync(RejectionReason e)
    {
        e.Code = Required(e.Code, "El código");
        e.Name = Required(e.Name, "El nombre");
        if (!RejectionAppliesTo.All.Contains(e.AppliesTo))
            throw new InvalidOperationException($"Valor inválido para 'aplica a': '{e.AppliesTo}'.");
        return await SaveAsync(e, x => x.Code == e.Code, $"Ya existe un motivo con el código '{e.Code}'.");
    }

    public Task<DeleteResult> DeleteRejectionReasonAsync(long id) => DeleteAsync(_db.RejectionReasons, id, async () =>
        await _db.ManifestDocuments.AnyAsync(d => d.CompanyId == CompanyId && d.RejectionReasonId == id)
            ? "El motivo está usado en manifiestos; desactívelo en vez de eliminarlo."
            : null);

    // ---------------------------------------------------------------- Feriados

    public async Task<IReadOnlyList<Holiday>> ListHolidaysAsync(bool includeInactive = false) =>
        await _db.Holidays.Where(h => h.CompanyId == CompanyId).OrderBy(h => h.Date).ToListAsync();

    public async Task<long> SaveHolidayAsync(Holiday e)
    {
        e.Name = Required(e.Name, "El nombre");
        return await SaveAsync(e, x => x.Date == e.Date, $"Ya existe un feriado el {e.Date:dd-MM-yyyy}.");
    }

    public Task<DeleteResult> DeleteHolidayAsync(long id) => DeleteAsync(_db.Holidays, id, null);

    // ---------------------------------------------------------------- Destinatarios de avisos

    public async Task<IReadOnlyList<NotificationRecipient>> ListNotificationRecipientsAsync(bool includeInactive = false) =>
        await Scoped(_db.NotificationRecipients, includeInactive, r => r.IsActive).OrderBy(r => r.Id).ToListAsync();

    public async Task<long> SaveNotificationRecipientAsync(NotificationRecipient e)
    {
        if (e.UserId == Guid.Empty) throw new InvalidOperationException("Debe indicar el usuario.");
        return await SaveAsync(e, x => x.UserId == e.UserId, "Ese usuario ya está en la lista de destinatarios.");
    }

    public Task<DeleteResult> DeleteNotificationRecipientAsync(long id) => DeleteAsync(_db.NotificationRecipients, id, null);

    // ---------------------------------------------------------------- Comunas

    public async Task<IReadOnlyList<Commune>> ListCommunesAsync()
    {
        // Siembra perezosa e idempotente por compañía: la primera lectura carga el catálogo.
        await CommuneSeeder.EnsureSeededAsync(_db, CompanyId);
        return await _db.Communes.Where(c => c.CompanyId == CompanyId)
            .OrderBy(c => c.RegionCode).ThenBy(c => c.Name).ToListAsync();
    }

    public async Task<Commune?> FindCommuneByNameAsync(string name)
    {
        var wanted = CommuneNames.Normalize(name);
        if (wanted.Length == 0) return null;
        var communes = await LoadCommunesAsync();
        var official = communes.FirstOrDefault(c => CommuneNames.Normalize(c.Name) == wanted);
        if (official is not null) return official;

        var alias = await _db.CommuneAliases.AsNoTracking()
            .FirstOrDefaultAsync(a => a.CompanyId == CompanyId && a.NormalizedText == wanted);
        return alias is null ? null : communes.FirstOrDefault(c => c.Id == alias.CommuneId);
    }

    public async Task<IReadOnlyList<CommuneAlias>> ListCommuneAliasesAsync() =>
        await _db.CommuneAliases.Where(a => a.CompanyId == CompanyId).OrderBy(a => a.NormalizedText).ToListAsync();

    public async Task<long> SaveCommuneAliasAsync(CommuneAlias e)
    {
        e.Text = Required(e.Text, "El texto");
        if (e.Text.Length > 60) throw new InvalidOperationException("El texto admite hasta 60 caracteres.");
        e.NormalizedText = CommuneNames.Normalize(e.Text);
        if (e.NormalizedText.Length == 0) throw new InvalidOperationException("El texto es obligatorio.");
        if (e.CommuneId == 0) throw new InvalidOperationException("La comuna es obligatoria.");
        await EnsureExistsAsync<Commune>(e.CommuneId, "La comuna");
        var key = e.NormalizedText;
        var official = (await LoadCommunesAsync()).FirstOrDefault(c => CommuneNames.Normalize(c.Name) == key);
        if (official is not null)
            throw new InvalidOperationException($"'{e.Text}' ya se reconoce como la comuna {official.Name}; no necesita equivalencia.");
        return await SaveAsync(e, x => x.NormalizedText == key, $"Ya existe una equivalencia para '{e.Text}'.");
    }

    public Task<DeleteResult> DeleteCommuneAliasAsync(long id) => DeleteAsync(_db.CommuneAliases, id, null);

    public async Task RememberCommuneAliasAsync(string? sourceText, long communeId)
    {
        var key = CommuneNames.Normalize(sourceText);
        if (key.Length == 0) return;
        // Un nombre oficial (o variante de fábrica) no se convierte en equivalencia: se reconoce solo.
        if ((await LoadCommunesAsync()).Any(c => CommuneNames.Normalize(c.Name) == key)) return;

        var now = DateTime.UtcNow;
        var existing = await _db.CommuneAliases.FirstOrDefaultAsync(a => a.CompanyId == CompanyId && a.NormalizedText == key);
        if (existing is null)
        {
            var text = sourceText!.Trim();
            _db.CommuneAliases.Add(new CommuneAlias
            {
                CompanyId = CompanyId, Text = text.Length > 60 ? text[..60] : text, NormalizedText = key,
                CommuneId = communeId, CreatedAt = now, CreatedBy = _user.UserId,
            });
        }
        else if (existing.CommuneId != communeId)
        {
            existing.CommuneId = communeId;
            existing.UpdatedAt = now;
            existing.UpdatedBy = _user.UserId;
        }
        else return;
        await _db.SaveChangesAsync();
    }

    private async Task<List<Commune>> LoadCommunesAsync()
    {
        await CommuneSeeder.EnsureSeededAsync(_db, CompanyId);
        // El catálogo son ~346 filas: se compara en memoria porque la intercalación de la base
        // (Postgres vs SQL Server) no garantiza ignorar tildes.
        return await _db.Communes.Where(c => c.CompanyId == CompanyId).ToListAsync();
    }

    // ---------------------------------------------------------------- Helpers

    private IQueryable<T> Scoped<T>(DbSet<T> set, bool includeInactive, Expression<Func<T, bool>> active)
        where T : AuditedEntity
    {
        var q = set.Where(x => x.CompanyId == CompanyId);
        return includeInactive ? q : q.Where(active);
    }

    private static string Required(string? value, string label)
    {
        var v = value?.Trim() ?? "";
        if (v.Length == 0) throw new InvalidOperationException($"{label} es obligatorio.");
        return v;
    }

    private static string NormalizeRut(string? rut)
    {
        if (!RutChileno.IsValid(rut)) throw new InvalidOperationException($"El RUT '{rut}' no es válido.");
        return RutChileno.Normalize(rut!);
    }

    private async Task EnsureExistsAsync<T>(long? id, string label) where T : AuditedEntity
    {
        if (id is null) return;
        if (!await _db.Set<T>().AnyAsync(x => x.Id == id && x.CompanyId == CompanyId))
            throw new InvalidOperationException($"{label} seleccionado no existe.");
    }

    /// <summary>
    /// Inserta (Id == 0) o actualiza un maestro de la compañía activa. <paramref name="duplicate"/>
    /// detecta repetidos dentro de la compañía (excluyendo el propio registro).
    /// </summary>
    private async Task<long> SaveAsync<T>(
        T incoming,
        Expression<Func<T, bool>> duplicate,
        string duplicateMessage,
        Func<IQueryable<T>, IQueryable<T>>? include = null,
        Action<T, T>? syncChildren = null)
        where T : AuditedEntity
    {
        var set = _db.Set<T>();
        var now = DateTime.UtcNow;

        if (await set.Where(x => x.CompanyId == CompanyId && x.Id != incoming.Id).AnyAsync(duplicate))
            throw new InvalidOperationException(duplicateMessage);

        if (incoming.Id == 0)
        {
            incoming.CompanyId = CompanyId;
            incoming.CreatedAt = now;
            incoming.CreatedBy = _user.UserId;
            set.Add(incoming);
            await _db.SaveChangesAsync();
            return incoming.Id;
        }

        IQueryable<T> query = set.Where(x => x.CompanyId == CompanyId);
        if (include is not null) query = include(query);
        var existing = await query.FirstOrDefaultAsync(x => x.Id == incoming.Id)
            ?? throw new InvalidOperationException("El registro no existe.");

        // Copia los valores editables y conserva compañía y auditoría de creación.
        var (companyId, createdAt, createdBy) = (existing.CompanyId, existing.CreatedAt, existing.CreatedBy);
        _db.Entry(existing).CurrentValues.SetValues(incoming);
        existing.CompanyId = companyId;
        existing.CreatedAt = createdAt;
        existing.CreatedBy = createdBy;
        existing.UpdatedAt = now;
        existing.UpdatedBy = _user.UserId;
        syncChildren?.Invoke(existing, incoming);

        await _db.SaveChangesAsync();
        return existing.Id;
    }

    private async Task<DeleteResult> DeleteAsync<T>(DbSet<T> set, long id, Func<Task<string?>>? blockReason)
        where T : AuditedEntity
    {
        var entity = await set.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        if (entity is null) return new DeleteResult(false, "El registro no existe.");

        var reason = blockReason is null ? null : await blockReason();
        if (reason is not null) return new DeleteResult(false, reason);

        set.Remove(entity);
        await _db.SaveChangesAsync();
        return new DeleteResult(true, null);
    }
}
