using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Servicios;

public class ManifestService : IManifestService
{
    private const int MaxNumberAttempts = 3;

    // Payloads legibles en la bitácora (tildes sin escapar).
    private static readonly JsonSerializerOptions PayloadJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly DespachosDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly ISapDocumentLookupService _sap;
    private readonly IMasterDataService _master;
    private readonly IManifestCostService _cost;
    private readonly TimeProvider _time;

    public ManifestService(
        DespachosDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user,
        ISapDocumentLookupService sap, IMasterDataService master, IManifestCostService cost, TimeProvider time)
    {
        _db = db;
        _company = company;
        _user = user;
        _sap = sap;
        _master = master;
        _cost = cost;
        _time = time;
    }

    private Guid CompanyId => _company.CompanyId;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    /// <summary>Punto de extensión de las pruebas: simula la violación del índice único (company_id, number).</summary>
    protected virtual Task SaveNewManifestAsync() => _db.SaveChangesAsync();

    // ---------------------------------------------------------------- Borrador

    public async Task<Guid> CreateDraftAsync(ManifestHeaderInput input)
    {
        await ValidateHeaderAsync(input);
        for (var attempt = 1; ; attempt++)
        {
            var now = UtcNow;
            var m = new Manifest
            {
                Id = Guid.NewGuid(), CompanyId = CompanyId, Number = await NextNumberAsync(),
                Status = ManifestStatuses.Draft, CostStatus = CostStatuses.NoRate,
                CreatedAt = now, CreatedBy = _user.UserId,
            };
            ApplyHeader(m, input);
            _db.Manifests.Add(m);
            AddEvent(m.Id, ManifestEventTypes.Created, null, null, ManifestStatuses.Draft, new { number = m.Number });
            try
            {
                await SaveNewManifestAsync();
                return m.Id;
            }
            catch (DbUpdateException) when (attempt < MaxNumberAttempts)
            {
                // Otro usuario tomó el mismo número entre la lectura y la escritura: se descarta y se reintenta.
                foreach (var e in _db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                    e.State = EntityState.Detached;
            }
        }
    }

    private async Task<int> NextNumberAsync() =>
        (await _db.Manifests.Where(x => x.CompanyId == CompanyId).MaxAsync(x => (int?)x.Number) ?? 0) + 1;

    public async Task UpdateDraftHeaderAsync(Guid manifestId, ManifestHeaderInput input)
    {
        var m = await LoadAsync(manifestId);
        RequireStatus(m, ManifestStatuses.Draft, "Solo se puede editar la cabecera de un manifiesto en borrador.");
        await ValidateHeaderAsync(input);
        ApplyHeader(m, input);
        m.UpdatedAt = UtcNow;
        m.UpdatedBy = _user.UserId;
        await _db.SaveChangesAsync();
    }

    private static void ApplyHeader(Manifest m, ManifestHeaderInput i)
    {
        m.DispatchDate = i.DispatchDate;
        m.ShiftId = i.ShiftId;
        m.OriginId = i.OriginId;
        m.CarrierId = i.CarrierId;
        m.VehicleId = i.VehicleId;
        m.TrailerId = i.TrailerId;
        m.DriverId = i.DriverId;
        m.EstimatedArrivalDate = i.EstimatedArrivalDate;
        m.EstimatedArrivalTime = i.EstimatedArrivalTime;
        m.PlannedDepartureTime = i.PlannedDepartureTime;
        m.ServiceOrder = string.IsNullOrWhiteSpace(i.ServiceOrder) ? null : i.ServiceOrder.Trim();
        m.Remarks = string.IsNullOrWhiteSpace(i.Remarks) ? null : i.Remarks.Trim();
        m.OdometerStart = i.OdometerStart;
    }

    private async Task ValidateHeaderAsync(ManifestHeaderInput i)
    {
        if (!await _db.Origins.AnyAsync(x => x.CompanyId == CompanyId && x.Id == i.OriginId))
            throw new InvalidOperationException("El origen seleccionado no existe.");
        if (!await _db.Carriers.AnyAsync(x => x.CompanyId == CompanyId && x.Id == i.CarrierId))
            throw new InvalidOperationException("El transportista seleccionado no existe.");
        if (!await _db.Vehicles.AnyAsync(x => x.CompanyId == CompanyId && x.Id == i.VehicleId))
            throw new InvalidOperationException("El vehículo seleccionado no existe.");
        if (!await _db.Drivers.AnyAsync(x => x.CompanyId == CompanyId && x.Id == i.DriverId))
            throw new InvalidOperationException("El chofer seleccionado no existe.");
        if (i.TrailerId != null
            && !await _db.Vehicles.AnyAsync(x => x.CompanyId == CompanyId && x.Id == i.TrailerId && x.IsTrailer))
            throw new InvalidOperationException("El carro seleccionado no existe.");
        if (i.ShiftId != null && !await _db.Shifts.AnyAsync(x => x.CompanyId == CompanyId && x.Id == i.ShiftId))
            throw new InvalidOperationException("El turno seleccionado no existe.");
    }

    // ---------------------------------------------------------------- Documentos

    public async Task<AddDocumentResult> AddDocumentAsync(Guid manifestId, DocumentCapture capture)
    {
        var m = await LoadAsync(manifestId);
        RequireStatus(m, ManifestStatuses.Draft, "Solo se pueden agregar documentos a un manifiesto en borrador.");
        var warnings = new List<string>();

        string kind;
        long folio;
        int? sii;
        TedData? ted = null;
        string captureMethod;

        if (!string.IsNullOrWhiteSpace(capture.RawScan))
        {
            if (!TedParser.TryParse(capture.RawScan, out var parsed))
                return Result(AddDocumentOutcomes.InvalidScan, "No se pudo leer el timbre del documento; reintente el escaneo o digite el folio.");
            ted = parsed;

            var issuer = await _master.GetIssuerTaxIdAsync();
            if (string.IsNullOrWhiteSpace(issuer))
                throw new InvalidOperationException("Configure el RUT emisor de la compañía en Configuración > Orígenes.");
            if (!SameRut(issuer, parsed.IssuerRut))
                return Result(AddDocumentOutcomes.OtherIssuer, $"El documento fue emitido por otro RUT ({parsed.IssuerRut}), no por esta compañía.");

            try { kind = SapDocumentLookupService.DocumentKindFor(parsed.DocumentType); }
            catch (InvalidOperationException ex) { return Result(AddDocumentOutcomes.InvalidScan, ex.Message); }
            folio = parsed.Folio;
            sii = parsed.DocumentType;
            captureMethod = CaptureMethods.Pdf417;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(capture.DocumentKind) || !DocumentKinds.All.Contains(capture.DocumentKind)
                || capture.Folio is not > 0)
                return Result(AddDocumentOutcomes.InvalidScan, "Indique el tipo de documento y un folio válido.");
            kind = capture.DocumentKind;
            folio = capture.Folio.Value;
            sii = capture.SiiDocumentType;
            captureMethod = CaptureMethods.Manual;
        }

        // Escaneo repetido: aviso, no error (no se consulta SAP).
        if (m.Documents.Any(d => d.DocumentKind == kind && d.Folio == folio && (sii == null || d.SiiDocumentType == sii)))
            return Result(AddDocumentOutcomes.DuplicateInThis, $"El folio {folio} ya está en este manifiesto.");

        SapDocumentInfo? info;
        try { info = await _sap.FindAsync(kind, folio, sii); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("más de un tipo de documento", StringComparison.Ordinal))
        {
            return Result(AddDocumentOutcomes.Ambiguous, ex.Message);
        }

        if (info is null)
            return Result(AddDocumentOutcomes.NotFound, $"No se encontró el folio {folio} en SAP.");
        if (info.IsCanceled)
            return Result(AddDocumentOutcomes.Canceled, $"El folio {folio} está anulado en SAP.");
        if (m.Documents.Any(d => d.DocumentKind == info.DocumentKind && d.SiiDocumentType == info.SiiDocumentType && d.Folio == info.Folio))
            return Result(AddDocumentOutcomes.DuplicateInThis, $"El folio {folio} ya está en este manifiesto.");

        var other = await FindActiveElsewhereAsync(info, m.Id, includeDrafts: true);
        if (other != null)
            return new AddDocumentResult(AddDocumentOutcomes.InOtherManifest, null, other,
                new[] { $"El folio {folio} ya está en el manifiesto N° {other}." });

        if (ted != null && ted.Amount != info.TotalAmount)
            warnings.Add($"El monto del timbre ({ted.Amount:N0}) no coincide con el de SAP ({info.TotalAmount:N0}).");
        if (info.DocumentDate > m.DispatchDate)
            warnings.Add($"La fecha del documento ({info.DocumentDate:dd-MM-yyyy}) es posterior a la fecha de despacho.");

        Commune? commune = string.IsNullOrWhiteSpace(info.DestinationCommune)
            ? null : await _master.FindCommuneByNameAsync(info.DestinationCommune);
        if (commune is null)
            warnings.Add($"La comuna de destino '{info.DestinationCommune}' no se pudo resolver; corríjala antes de la guía de transporte (el costo queda sin tarifa).");

        var doc = new ManifestDocument
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ManifestId = m.Id,
            DocumentKind = info.DocumentKind, SiiDocumentType = info.SiiDocumentType, Folio = info.Folio,
            SapDocEntry = info.DocEntry, SapDocNum = info.DocNum, DocumentDate = info.DocumentDate,
            TotalAmount = info.TotalAmount,
            CustomerCode = info.CustomerCode, CustomerName = info.CustomerName, CustomerTaxId = info.CustomerTaxId,
            DestinationAddress = info.DestinationAddress, DestinationCommuneName = info.DestinationCommune,
            DestinationCommuneId = commune?.Id,
            SapBaseOrderEntry = info.BaseOrderEntry, CustomerPo = info.CustomerPo,
            Units = info.Units, Packages = info.Packages, WeightKg = info.WeightKg, VolumeM3 = info.VolumeM3,
            DeliveryStatus = DeliveryStatuses.Pending, CaptureMethod = captureMethod,
            AddedAt = UtcNow, AddedBy = _user.UserId,
        };
        _db.ManifestDocuments.Add(doc); // el fixup de EF lo agrega a m.Documents
        AddEvent(m.Id, ManifestEventTypes.DocumentAdded, doc.Id, null, null,
            new { kind = doc.DocumentKind, sii = doc.SiiDocumentType, folio = doc.Folio, method = captureMethod });
        await _db.SaveChangesAsync();
        return new AddDocumentResult(AddDocumentOutcomes.Added, doc.Id, null, warnings);

        static AddDocumentResult Result(string outcome, string message) =>
            new(outcome, null, null, new[] { message });
    }

    public async Task RemoveDocumentAsync(Guid manifestId, Guid manifestDocumentId)
    {
        var m = await LoadAsync(manifestId);
        RequireStatus(m, ManifestStatuses.Draft, "Solo se pueden quitar documentos de un manifiesto en borrador.");
        var doc = m.Documents.FirstOrDefault(d => d.Id == manifestDocumentId)
            ?? throw new InvalidOperationException("El documento no está en este manifiesto.");
        _db.ManifestDocuments.Remove(doc);
        AddEvent(m.Id, ManifestEventTypes.DocumentRemoved, doc.Id, null, null,
            new { kind = doc.DocumentKind, sii = doc.SiiDocumentType, folio = doc.Folio });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Número del otro manifiesto que ocupa el documento, o null. "Ocupa" = manifiesto no anulado y documento no
    /// marcado no_entregado. En el despacho (includeDrafts = false) los borradores ajenos no cuentan: gana quien
    /// despacha primero.
    /// </summary>
    private async Task<int?> FindActiveElsewhereAsync(SapDocumentInfo info, Guid exceptManifestId, bool includeDrafts) =>
        await FindActiveElsewhereAsync(info.DocumentKind, info.SiiDocumentType, info.Folio, exceptManifestId, includeDrafts);

    private async Task<int?> FindActiveElsewhereAsync(string kind, int sii, long folio, Guid exceptManifestId, bool includeDrafts) =>
        await (from d in _db.ManifestDocuments
               join o in _db.Manifests on d.ManifestId equals o.Id
               where d.CompanyId == CompanyId && o.CompanyId == CompanyId && o.Id != exceptManifestId
                     && d.DocumentKind == kind && d.SiiDocumentType == sii && d.Folio == folio
                     && o.Status != ManifestStatuses.Annulled
                     && (includeDrafts || o.Status != ManifestStatuses.Draft)
                     && d.DeliveryStatus != DeliveryStatuses.NotDelivered
               orderby o.Number
               select (int?)o.Number).FirstOrDefaultAsync();

    private static bool SameRut(string a, string b)
    {
        try { return RutChileno.Normalize(a) == RutChileno.Normalize(b); }
        catch (ArgumentException) { return false; }
    }

    // ---------------------------------------------------------------- Despacho

    public async Task<DispatchResult> DispatchAsync(Guid manifestId)
    {
        var m = await LoadAsync(manifestId);
        RequireStatus(m, ManifestStatuses.Draft, "Solo se puede despachar un manifiesto en borrador.");
        var errors = new List<string>();
        var warnings = new List<string>();

        var carrier = await _db.Carriers.FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == m.CarrierId);
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == m.VehicleId);
        var trailer = m.TrailerId == null ? null
            : await _db.Vehicles.FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == m.TrailerId);
        var driver = await _db.Drivers.FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == m.DriverId);
        var origin = await _db.Origins.FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == m.OriginId);

        if (carrier is not { IsActive: true }) errors.Add("El transportista no existe o está inactivo.");
        if (vehicle is not { IsActive: true }) errors.Add("El vehículo no existe o está inactivo.");
        if (m.TrailerId != null && trailer is not { IsActive: true }) errors.Add("El carro no existe o está inactivo.");
        if (driver is not { IsActive: true }) errors.Add("El chofer no existe o está inactivo.");
        if (origin is null) errors.Add("El origen no existe.");

        if (carrier != null && carrier.Kind != CarrierKinds.Pickup && !RutChileno.IsValid(carrier.TaxId))
            errors.Add("El RUT del transportista no es válido.");
        if (driver != null && !RutChileno.IsValid(driver.TaxId))
            errors.Add("El RUT del chofer no es válido.");
        if (m.Documents.Count == 0)
            errors.Add("El manifiesto debe tener al menos un documento.");

        // Revalida contra otros manifiestos: el chequeo al agregar no cubre dos usuarios simultáneos.
        foreach (var d in m.Documents)
        {
            var other = await FindActiveElsewhereAsync(d.DocumentKind, d.SiiDocumentType, d.Folio, m.Id, includeDrafts: false);
            if (other != null)
                errors.Add($"El folio {d.Folio} ya está despachado en el manifiesto N° {other}; quítelo de este manifiesto.");
        }

        warnings.AddRange(await CarrierWarningsAsync(m.CarrierId, vehicle, driver));
        if (driver?.LicenseExpiresAt is { } exp && exp < m.DispatchDate)
            warnings.Add($"La licencia del chofer venció el {exp:dd-MM-yyyy}.");

        if (errors.Count > 0) return new DispatchResult(false, errors, warnings);

        var now = UtcNow;
        m.Status = ManifestStatuses.Dispatched;
        m.DepartedAt = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        await TakeSnapshotAsync(m, carrier!, vehicle!, trailer, driver!, origin!);
        m.TotalDocuments = m.Documents.Count;
        m.TotalUnits = m.Documents.Sum(d => d.Units);
        m.TotalPackages = m.Documents.Sum(d => d.Packages);
        m.UpdatedAt = now;
        m.UpdatedBy = _user.UserId;

        AddEvent(m.Id, ManifestEventTypes.Dispatched, null, ManifestStatuses.Draft, ManifestStatuses.Dispatched,
            new { departedAtUtc = m.DepartedAt, documents = m.TotalDocuments });
        foreach (var d in m.Documents)
        {
            d.DeliveryStatus = DeliveryStatuses.Dispatched;
            AddEvent(m.Id, ManifestEventTypes.Dispatched, d.Id, DeliveryStatuses.Pending, DeliveryStatuses.Dispatched, null);
        }

        // Recalcula y guarda todo en una sola escritura; si el cálculo falla no se despacha nada.
        var cost = await _cost.RecalculateAsync(m);
        if (cost.Status == CostStatuses.NoRate)
            warnings.Add("Sin tarifa para este viaje: se despacha igual, pero deberá corregir las comunas/tarifas o ingresar un costo manual con motivo antes del cierre mensual.");
        else if (cost.Status == CostStatuses.Ambiguous)
            warnings.Add("Tarifa ambigua para este viaje: revise el tarifario antes del cierre mensual.");
        return new DispatchResult(true, Array.Empty<string>(), warnings);
    }

    private async Task TakeSnapshotAsync(
        Manifest m, Carrier carrier, Vehicle vehicle, Vehicle? trailer, Driver driver, Origin origin)
    {
        m.CarrierTaxId = carrier.TaxId;
        m.DriverTaxId = driver.TaxId;
        m.DriverName = driver.FullName;
        m.Plate = vehicle.Plate;
        m.TrailerPlate = trailer?.Plate;
        m.OriginAddress = origin.Address;
        m.OriginCommune = origin.CommuneId == null ? null
            : (await _db.Communes.FirstOrDefaultAsync(c => c.CompanyId == CompanyId && c.Id == origin.CommuneId))?.Name;
    }

    public async Task<IReadOnlyList<string>> ChangeVehicleOrDriverAsync(Guid manifestId, long vehicleId, long? trailerId, long driverId, string reason)
    {
        var m = await LoadAsync(manifestId);
        if (m.Status != ManifestStatuses.Dispatched)
            throw new InvalidOperationException(
                m.Status == ManifestStatuses.Draft
                    ? "En borrador edite la cabecera del manifiesto; el cambio con motivo aplica a manifiestos despachados."
                    : "Solo se puede cambiar el vehículo o el chofer de un manifiesto despachado.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Indique el motivo del cambio.");

        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == vehicleId && x.IsActive)
            ?? throw new InvalidOperationException("El vehículo seleccionado no existe o está inactivo.");
        Vehicle? trailer = null;
        if (trailerId != null)
            trailer = await _db.Vehicles.FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == trailerId && x.IsTrailer && x.IsActive)
                ?? throw new InvalidOperationException("El carro seleccionado no existe o está inactivo.");
        var driver = await _db.Drivers.FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == driverId && x.IsActive)
            ?? throw new InvalidOperationException("El chofer seleccionado no existe o está inactivo.");
        if (!RutChileno.IsValid(driver.TaxId))
            throw new InvalidOperationException("El RUT del chofer no es válido.");

        // El transportista del manifiesto no cambia nunca de forma implícita (define pago y tarifa).
        var carrier = await _db.Carriers.FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == m.CarrierId)
            ?? throw new InvalidOperationException("El transportista del manifiesto no existe.");
        var origin = await _db.Origins.FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == m.OriginId)
            ?? throw new InvalidOperationException("El origen no existe.");

        var before = new { plate = m.Plate, trailerPlate = m.TrailerPlate, driver = m.DriverName, driverTaxId = m.DriverTaxId, carrierId = m.CarrierId };
        m.VehicleId = vehicle.Id;
        m.TrailerId = trailer?.Id;
        m.DriverId = driver.Id;
        await TakeSnapshotAsync(m, carrier, vehicle, trailer, driver, origin);
        m.UpdatedAt = UtcNow;
        m.UpdatedBy = _user.UserId;
        AddEvent(m.Id, ManifestEventTypes.VehicleChanged, null, m.Status, m.Status, new
        {
            reason = reason.Trim(), before,
            after = new { plate = m.Plate, trailerPlate = m.TrailerPlate, driver = m.DriverName, driverTaxId = m.DriverTaxId, carrierId = m.CarrierId },
        });
        await _cost.RecalculateAsync(m);
        return await CarrierWarningsAsync(m.CarrierId, vehicle, driver);
    }

    /// <summary>Advertencias (no errores): vehículo de otro transportista que el del manifiesto; chofer de otro que el del vehículo.</summary>
    private async Task<List<string>> CarrierWarningsAsync(long manifestCarrierId, Vehicle? vehicle, Driver? driver)
    {
        var list = new List<string>();
        if (vehicle != null && vehicle.CarrierId != manifestCarrierId)
        {
            var name = vehicle.CarrierId == null ? "sin transportista"
                : (await _db.Carriers.FirstOrDefaultAsync(c => c.CompanyId == CompanyId && c.Id == vehicle.CarrierId))?.Name ?? "otro transportista";
            list.Add($"El vehículo {vehicle.Plate} pertenece a {name}, distinto del transportista del manifiesto.");
        }
        if (vehicle != null && driver != null && driver.CarrierId != vehicle.CarrierId)
            list.Add($"El chofer {driver.FullName} no pertenece al transportista del vehículo.");
        return list;
    }

    // ---------------------------------------------------------------- Consulta

    public async Task<Manifest?> GetAsync(Guid manifestId) =>
        await _db.Manifests
            .Include(x => x.Documents).Include(x => x.CostLines).Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == manifestId && x.CompanyId == CompanyId);

    public async Task<IReadOnlyList<ManifestListItem>> ListAsync(ManifestFilter filter)
    {
        var q = _db.Manifests.Where(m => m.CompanyId == CompanyId);
        if (filter.From != null) q = q.Where(m => m.DispatchDate >= filter.From);
        if (filter.To != null) q = q.Where(m => m.DispatchDate <= filter.To);
        if (!string.IsNullOrWhiteSpace(filter.Status)) q = q.Where(m => m.Status == filter.Status);
        if (filter.CarrierId != null) q = q.Where(m => m.CarrierId == filter.CarrierId);
        if (!string.IsNullOrWhiteSpace(filter.FolioContains))
        {
            var f = filter.FolioContains.Trim();
            q = q.Where(m => _db.ManifestDocuments.Any(d => d.ManifestId == m.Id && d.Folio.ToString().Contains(f)));
        }

        var rows = await q.OrderByDescending(m => m.Number).ToListAsync();
        var ids = rows.Select(r => r.Id).ToList();
        var counts = (await _db.ManifestDocuments.Where(d => d.CompanyId == CompanyId && ids.Contains(d.ManifestId))
                .GroupBy(d => d.ManifestId).Select(g => new { g.Key, N = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.N);
        var carriers = await _db.Carriers.Where(c => c.CompanyId == CompanyId).ToDictionaryAsync(c => c.Id, c => c.Name);
        var plates = await _db.Vehicles.Where(v => v.CompanyId == CompanyId).ToDictionaryAsync(v => v.Id, v => v.Plate);

        return rows.Select(m => new ManifestListItem(
            m.Id, m.Number, m.Status, m.DispatchDate, m.CarrierId, carriers.GetValueOrDefault(m.CarrierId, "—"),
            m.Plate ?? plates.GetValueOrDefault(m.VehicleId), counts.GetValueOrDefault(m.Id), m.CostFinal, m.CostStatus)).ToList();
    }

    // ---------------------------------------------------------------- Helpers

    private async Task<Manifest> LoadAsync(Guid id) =>
        await _db.Manifests.Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId)
        ?? throw new InvalidOperationException("El manifiesto no existe.");

    private static void RequireStatus(Manifest m, string status, string message)
    {
        if (m.Status != status) throw new InvalidOperationException(message);
    }

    private void AddEvent(Guid manifestId, string type, Guid? documentId, string? from, string? to, object? payload) =>
        _db.ManifestEvents.Add(new ManifestEvent
        {
            CompanyId = CompanyId, ManifestId = manifestId, ManifestDocumentId = documentId, EventType = type,
            FromStatus = from, ToStatus = to, UserId = _user.UserId, OccurredAt = UtcNow,
            PayloadJson = payload == null ? null : JsonSerializer.Serialize(payload, PayloadJson),
        });
}
