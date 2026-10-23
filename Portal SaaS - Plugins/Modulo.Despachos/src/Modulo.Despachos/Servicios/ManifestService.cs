using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modulo.Despachos.Data;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

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
    private readonly INotificationService _notifications;
    private readonly IMonthlyClosureService _closure;
    private readonly ILogger<ManifestService>? _logger;
    private readonly ITransportGuideService? _guides;

    public ManifestService(
        DespachosDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user,
        ISapDocumentLookupService sap, IMasterDataService master, IManifestCostService cost, TimeProvider time,
        INotificationService notifications, IMonthlyClosureService closure, ILogger<ManifestService>? logger = null,
        ITransportGuideService? guides = null)
    {
        _logger = logger;
        _guides = guides;
        _db = db;
        _company = company;
        _user = user;
        _sap = sap;
        _master = master;
        _cost = cost;
        _time = time;
        _notifications = notifications;
        _closure = closure;
    }

    private Guid CompanyId => _company.CompanyId;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    /// <summary>Punto de extensión de las pruebas: simula la violación del índice único (company_id, number).</summary>
    protected virtual Task SaveNewManifestAsync() => _db.SaveChangesAsync();

    // ---------------------------------------------------------------- Borrador

    public async Task<Guid> CreateDraftAsync(ManifestHeaderInput input)
    {
        await ValidateHeaderAsync(input);
        await RequireOpenPeriodAsync(input.DispatchDate);
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
        await RequireOpenPeriodAsync(input.DispatchDate);
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
        if (!await _db.Vehicles.AnyAsync(x => x.CompanyId == CompanyId && x.Id == i.VehicleId && !x.IsTrailer))
            throw new InvalidOperationException("El vehículo seleccionado no existe o es un carro.");
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
            {
                // Hoja de agrupación (AT-000123): entran todas sus transferencias.
                if (DispatchGroupCode.TryParse(capture.RawScan, out var groupNumber))
                    return await AddDispatchGroupAsync(m, groupNumber);
                // No es un timbre: puede ser el LPN de una transferencia a sucursal (Fase B).
                var lpn = await LpnFromScanAsync(capture.RawScan);
                if (lpn is null)
                    return Result(AddDocumentOutcomes.InvalidScan, "No se pudo leer el timbre del documento; reintente el escaneo o digite el folio.");
                var transfer = await _sap.FindTransferByLpnAsync(lpn);
                if (transfer is null)
                    return Result(AddDocumentOutcomes.NotFound, $"No se encontró el LPN {lpn} en SAP.");
                return await AddTransferAsync(m, transfer, CaptureMethods.Pdf417);
            }
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
            warnings.Add($"La comuna de destino '{info.DestinationCommune}' no se pudo resolver; elíjala en la tabla de documentos (se recordará para las próximas facturas). Mientras tanto el costo queda sin tarifa y no se puede emitir la guía.");

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
    }

    public async Task<AddPendingTransfersResult> AddPendingTransfersAsync(Guid manifestId, string warehouseCode, DateOnly fromDate)
    {
        var m = await LoadAsync(manifestId);
        RequireStatus(m, ManifestStatuses.Draft, "Solo se pueden agregar documentos a un manifiesto en borrador.");
        var branch = await _master.FindBranchWarehouseAsync(warehouseCode);
        if (branch is not { IsActive: true })
            throw new InvalidOperationException($"La bodega {warehouseCode} no es una sucursal activa.");

        var added = 0;
        var skipped = new List<string>();
        foreach (var t in await _sap.ListPendingTransfersAsync(branch.WarehouseCode, fromDate))
        {
            var r = await AddTransferAsync(m, t, CaptureMethods.Manual);
            if (r.Outcome == AddDocumentOutcomes.Added) added++;
            else skipped.Add($"{t.DocNum}: {r.Warnings.FirstOrDefault()}");
        }
        return new AddPendingTransfersResult(added, skipped);
    }

    private static readonly Regex LpnShape = new(@"^[A-Z0-9_-]+$", RegexOptions.Compiled);

    /// <summary>LPN normalizado si el texto escaneado puede ser uno (una palabra y, si la compañía lo configuró, calzando con su patrón).</summary>
    private async Task<string?> LpnFromScanAsync(string raw)
    {
        var text = raw.Trim().ToUpperInvariant();
        // Un LPN es una sola palabra (letras, dígitos, '-' o '_'): un timbre roto o basura del lector no lo es.
        if (text.Length == 0 || text.Length > 30 || !LpnShape.IsMatch(text)) return null;
        var pattern = (await _master.GetSettingsAsync())?.LpnPattern;
        if (string.IsNullOrWhiteSpace(pattern)) return text;
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)) ? text : null;
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    /// Agrega una transferencia (objeto 67, controlada por DocEntry): anulada, a bodega no sucursal, ya en este
    /// manifiesto o en otro manifiesto no anulado se rechaza con su motivo.
    /// </summary>
    private async Task<AddDocumentResult> AddTransferAsync(Manifest m, SapTransferInfo t, string captureMethod, long? dispatchGroupId = null)
    {
        if (t.IsCanceled)
            return Result(AddDocumentOutcomes.Canceled, $"La transferencia {t.DocNum} está anulada en SAP.");
        var branch = await _master.FindBranchWarehouseAsync(t.WarehouseCode);
        if (branch is not { IsActive: true })
            return Result(AddDocumentOutcomes.NotBranch,
                $"La transferencia {t.DocNum} va a la bodega {t.WarehouseCode} ({t.WarehouseName}), que no es sucursal.");
        if (m.Documents.Any(d => d.DocumentKind == DocumentKinds.Transfer && d.SapDocEntry == t.DocEntry))
            return Result(AddDocumentOutcomes.DuplicateInThis, $"La transferencia {t.DocNum} ya está en este manifiesto.");
        var other = await FindActiveElsewhereAsync(DocumentKinds.Transfer, 0, t.DocNum, m.Id, includeDrafts: true, t.DocEntry);
        if (other != null)
            return new AddDocumentResult(AddDocumentOutcomes.InOtherManifest, null, other,
                new[] { $"La transferencia {t.DocNum} ya está en el manifiesto N° {other}." });
        if (dispatchGroupId is null && await ActiveGroupNumberOfAsync(t.DocEntry) is { } groupNumber)
            return Result(AddDocumentOutcomes.InDispatchGroup,
                $"El LPN {t.Lpn ?? t.DocNum.ToString()} pertenece a la agrupación {DispatchGroupCode.Format(groupNumber)}; escanee la hoja.");

        var warnings = new List<string>();
        var communeName = branch.CommuneName ?? t.DestinationCommune;
        var commune = string.IsNullOrWhiteSpace(communeName) ? null : await _master.FindCommuneByNameAsync(communeName);
        if (commune is null)
            warnings.Add($"La comuna de destino '{communeName}' no se pudo resolver; elíjala en la tabla de documentos (se recordará para las próximas). Mientras tanto el costo queda sin tarifa y no se puede emitir la guía.");

        var doc = new ManifestDocument
        {
            Id = Guid.NewGuid(), CompanyId = CompanyId, ManifestId = m.Id,
            DocumentKind = DocumentKinds.Transfer, SiiDocumentType = 0, Folio = t.DocNum,
            SapDocEntry = t.DocEntry, SapDocNum = t.DocNum, DocumentDate = t.DocumentDate, TotalAmount = 0,
            Lpn = t.Lpn is { Length: > 30 } l ? l[..30] : t.Lpn,
            CustomerCode = branch.WarehouseCode, CustomerName = branch.Name, CustomerTaxId = await _master.GetIssuerTaxIdAsync(),
            DestinationAddress = branch.Address ?? t.DestinationAddress, DestinationCommuneName = communeName,
            DestinationCommuneId = commune?.Id,
            Units = t.Units, Packages = 1, DispatchGroupId = dispatchGroupId,
            DeliveryStatus = DeliveryStatuses.Pending, CaptureMethod = captureMethod,
            AddedAt = UtcNow, AddedBy = _user.UserId,
        };
        _db.ManifestDocuments.Add(doc);
        AddEvent(m.Id, ManifestEventTypes.DocumentAdded, doc.Id, null, null,
            new { kind = doc.DocumentKind, folio = doc.Folio, lpn = doc.Lpn, method = captureMethod });
        await _db.SaveChangesAsync();
        return new AddDocumentResult(AddDocumentOutcomes.Added, doc.Id, null, warnings);
    }

    private static AddDocumentResult Result(string outcome, string message) => new(outcome, null, null, new[] { message });

    // ---------------------------------------------------------------- Agrupaciones

    /// <summary>
    /// Hoja de agrupación: revalida sus transferencias contra SAP con una sola consulta (pendientes de su sucursal) y
    /// agrega las vigentes con las reglas de siempre; las demás se informan y el resto entra igual.
    /// </summary>
    private async Task<AddDocumentResult> AddDispatchGroupAsync(Manifest m, int number)
    {
        var code = DispatchGroupCode.Format(number);
        var g = await _db.DispatchGroups.Include(x => x.Transfers)
            .FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Number == number);
        if (g is null) return Result(AddDocumentOutcomes.NotFound, $"No existe la agrupación {code}.");
        if (g.Status == DispatchGroupStatuses.Annulled) return Result(AddDocumentOutcomes.Canceled, $"La agrupación {code} está anulada.");
        var holder = await ManifestOfGroupAsync(g.Id);
        if (holder is { } h && h.Id != m.Id)
            return new AddDocumentResult(AddDocumentOutcomes.InOtherManifest, null, h.Number, new[]
            {
                g.Status == DispatchGroupStatuses.Dispatched
                    ? $"La agrupación {code} ya fue despachada en el manifiesto N° {h.Number}."
                    : $"La agrupación {code} ya está en el manifiesto N° {h.Number}.",
            });
        if (g.Transfers.Count == 0) return Result(AddDocumentOutcomes.NotFound, $"La agrupación {code} no tiene transferencias.");

        var current = (await _sap.ListPendingTransfersAsync(g.WarehouseCode, g.Transfers.Min(t => t.DocumentDate)))
            .GroupBy(t => t.DocEntry).ToDictionary(x => x.Key, x => x.First());
        var added = 0;
        var notes = new List<string>();
        foreach (var line in g.Transfers.OrderBy(t => t.SapDocNum))
        {
            if (!current.TryGetValue(line.SapDocEntry, out var t))
            {
                notes.Add($"La transferencia {line.SapDocNum} ya no está vigente en SAP.");
                continue;
            }
            var r = await AddTransferAsync(m, t, CaptureMethods.Pdf417, g.Id);
            if (r.Outcome == AddDocumentOutcomes.Added) added++;
            else if (r.Outcome != AddDocumentOutcomes.DuplicateInThis) notes.AddRange(r.Warnings.Take(1));
        }

        if (added == 0)
            return holder is not null && notes.Count == 0
                ? Result(AddDocumentOutcomes.DuplicateInThis, $"La agrupación {code} ya está en este manifiesto.")
                : new AddDocumentResult(AddDocumentOutcomes.NotFound, null, null,
                    new[] { $"{code}: no se agregó ninguna transferencia." }.Concat(notes).ToList());

        if (g.DispatchDate != m.DispatchDate)
            notes.Add($"La agrupación es para el {g.DispatchDate:dd-MM-yyyy} y el manifiesto sale el {m.DispatchDate:dd-MM-yyyy}.");
        await SyncDispatchGroupsAsync(new[] { g.Id });
        return new AddDocumentResult(AddDocumentOutcomes.GroupAdded, null, null,
            new[] { $"{code}: {added} transferencias agregadas." }.Concat(notes).ToList());
    }

    public async Task RemoveDispatchGroupAsync(Guid manifestId, long dispatchGroupId)
    {
        var m = await LoadAsync(manifestId);
        RequireStatus(m, ManifestStatuses.Draft, "Solo se pueden quitar documentos de un manifiesto en borrador.");
        var docs = m.Documents.Where(d => d.DispatchGroupId == dispatchGroupId).ToList();
        if (docs.Count == 0) throw new InvalidOperationException("La agrupación no está en este manifiesto.");
        foreach (var doc in docs)
        {
            _db.ManifestDocuments.Remove(doc);
            AddEvent(m.Id, ManifestEventTypes.DocumentRemoved, doc.Id, null, null,
                new { kind = doc.DocumentKind, folio = doc.Folio, group = dispatchGroupId });
        }
        await _db.SaveChangesAsync();
        await SyncDispatchGroupsAsync(new[] { dispatchGroupId });
    }

    /// <summary>Número de la agrupación (armada o en manifiesto) que retiene la transferencia, o null.</summary>
    private async Task<int?> ActiveGroupNumberOfAsync(int docEntry)
    {
        var holding = DispatchGroupStatuses.Holding.ToArray();
        return await (from t in _db.DispatchGroupTransfers
                      join g in _db.DispatchGroups on t.DispatchGroupId equals g.Id
                      where t.CompanyId == CompanyId && g.CompanyId == CompanyId && t.SapDocEntry == docEntry
                            && holding.Contains(g.Status)
                      orderby g.Number
                      select (int?)g.Number).FirstOrDefaultAsync();
    }

    /// <summary>Manifiesto no anulado que tiene documentos de la agrupación, o null.</summary>
    private async Task<(Guid Id, int Number)?> ManifestOfGroupAsync(long groupId)
    {
        var row = await (from d in _db.ManifestDocuments
                         join o in _db.Manifests on d.ManifestId equals o.Id
                         where d.CompanyId == CompanyId && d.DispatchGroupId == groupId && o.Status != ManifestStatuses.Annulled
                         orderby o.Number
                         select new { o.Id, o.Number }).FirstOrDefaultAsync();
        return row is null ? null : (row.Id, row.Number);
    }

    /// <summary>
    /// Estado de cada agrupación no anulada según sus documentos (ya guardados) en manifiestos no anulados: despachada si
    /// alguno salió, en manifiesto si hay alguno, armada si no queda ninguno.
    /// </summary>
    /// <remarks>
    /// Accesorio: corre después de guardar el manifiesto y nunca lanza (una falla no debe impedir la emisión de guías
    /// ni deshacer la operación); el estado se recalcula en la próxima operación sobre esos documentos.
    /// </remarks>
    private async Task SyncDispatchGroupsAsync(IEnumerable<long> groupIds)
    {
        try
        {
            await SyncDispatchGroupsCoreAsync(groupIds);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _logger?.LogWarning(ex, "No se pudo actualizar el estado de las agrupaciones.");
        }
    }

    private async Task SyncDispatchGroupsCoreAsync(IEnumerable<long> groupIds)
    {
        var ids = groupIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var groups = await _db.DispatchGroups
            .Where(g => g.CompanyId == CompanyId && ids.Contains(g.Id) && g.Status != DispatchGroupStatuses.Annulled)
            .ToListAsync();
        if (groups.Count == 0) return;
        var usage = await (from d in _db.ManifestDocuments
                           join o in _db.Manifests on d.ManifestId equals o.Id
                           where d.CompanyId == CompanyId && d.DispatchGroupId != null && ids.Contains(d.DispatchGroupId.Value)
                                 && o.Status != ManifestStatuses.Annulled
                           select new { GroupId = d.DispatchGroupId!.Value, o.Status }).ToListAsync();
        var departed = new[] { ManifestStatuses.Dispatched, ManifestStatuses.Returned, ManifestStatuses.Closed };
        foreach (var g in groups)
        {
            var statuses = usage.Where(u => u.GroupId == g.Id).Select(u => u.Status).ToList();
            var target = statuses.Any(s => departed.Contains(s)) ? DispatchGroupStatuses.Dispatched
                : statuses.Count > 0 ? DispatchGroupStatuses.InManifest
                : DispatchGroupStatuses.Armed;
            if (g.Status == target) continue;
            g.Status = target;
            g.UpdatedAt = UtcNow;
            g.UpdatedBy = _user.UserId;
        }
        await _db.SaveChangesAsync();
    }

    private static List<long> GroupIdsOf(Manifest m) =>
        m.Documents.Where(d => d.DispatchGroupId != null).Select(d => d.DispatchGroupId!.Value).Distinct().ToList();

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
        if (doc.DispatchGroupId is { } groupId) await SyncDispatchGroupsAsync(new[] { groupId });
    }

    /// <summary>
    /// Número del otro manifiesto que ocupa el documento, o null. "Ocupa" = manifiesto no anulado y documento no
    /// marcado no_entregado. En el despacho (includeDrafts = false) los borradores ajenos no cuentan: gana quien
    /// despacha primero.
    /// </summary>
    private async Task<int?> FindActiveElsewhereAsync(SapDocumentInfo info, Guid exceptManifestId, bool includeDrafts) =>
        await FindActiveElsewhereAsync(info.DocumentKind, info.SiiDocumentType, info.Folio, exceptManifestId, includeDrafts);

    /// <remarks>Las transferencias se controlan por DocEntry (el DocNum puede repetirse entre series).</remarks>
    private async Task<int?> FindActiveElsewhereAsync(string kind, int sii, long folio, Guid exceptManifestId, bool includeDrafts,
        int docEntry = 0)
    {
        var byEntry = kind == DocumentKinds.Transfer;
        return await (from d in _db.ManifestDocuments
               join o in _db.Manifests on d.ManifestId equals o.Id
               where d.CompanyId == CompanyId && o.CompanyId == CompanyId && o.Id != exceptManifestId
                     && d.DocumentKind == kind && d.SiiDocumentType == sii
                     && (byEntry ? d.SapDocEntry == docEntry : d.Folio == folio)
                     && o.Status != ManifestStatuses.Annulled
                     && (includeDrafts || o.Status != ManifestStatuses.Draft)
                     && d.DeliveryStatus != DeliveryStatuses.NotDelivered
               orderby o.Number
               select (int?)o.Number).FirstOrDefaultAsync();
    }

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
            errors.Add(string.IsNullOrWhiteSpace(carrier.TaxId)
                ? $"El transportista «{carrier.Name}» no tiene RUT registrado: complételo en Despachos > Transportistas."
                : $"El RUT del transportista «{carrier.Name}» ({carrier.TaxId}) no es válido: corríjalo en Despachos > Transportistas.");
        if (driver != null && !RutChileno.IsValid(driver.TaxId))
            errors.Add(string.IsNullOrWhiteSpace(driver.TaxId)
                ? $"El chofer «{driver.FullName}» no tiene RUT registrado: complételo en Despachos > Choferes."
                : $"El RUT del chofer «{driver.FullName}» ({driver.TaxId}) no es válido: corríjalo en Despachos > Choferes.");
        if (m.Documents.Count == 0)
            errors.Add("El manifiesto debe tener al menos un documento.");

        // Revalida contra otros manifiestos: el chequeo al agregar no cubre dos usuarios simultáneos.
        foreach (var d in m.Documents)
        {
            var other = await FindActiveElsewhereAsync(d.DocumentKind, d.SiiDocumentType, d.Folio, m.Id, includeDrafts: false, d.SapDocEntry);
            if (other != null)
                errors.Add($"El folio {d.Folio} ya está despachado en el manifiesto N° {other}; quítelo de este manifiesto.");
        }

        warnings.AddRange(await CarrierWarningsAsync(m.CarrierId, vehicle, driver));
        if (driver?.LicenseExpiresAt is { } exp && exp < m.DispatchDate)
            warnings.Add($"La licencia del chofer venció el {exp:dd-MM-yyyy}.");

        // Guías Res. 154: el plan se valida ANTES de cambiar nada (el camión no espera por datos que faltan).
        // Modo guías solo si hay algo que guiar: un manifiesto solo con guías del add-on despacha como antes.
        var withGuides = _guides != null && await _guides.IsEnabledAsync()
                         && m.Documents.Any(d => d.DocumentKind is DocumentKinds.Invoice or DocumentKinds.Transfer);
        if (withGuides && errors.Count == 0)
        {
            var preview = await _guides!.PreviewAsync(m.Id);
            errors.AddRange(preview.Errors);
            warnings.AddRange(preview.Warnings);
        }

        if (errors.Count > 0) return new DispatchResult(false, errors, warnings);

        var now = UtcNow;
        // Con guías, el manifiesto queda en "guías pendientes" hasta que todas estén emitidas.
        var target = withGuides ? ManifestStatuses.PendingGuides : ManifestStatuses.Dispatched;
        m.Status = target;
        m.Version++;
        m.DepartedAt = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        await TakeSnapshotAsync(m, carrier!, vehicle!, trailer, driver!, origin!);
        m.TotalDocuments = m.Documents.Count;
        m.TotalUnits = m.Documents.Sum(d => d.Units);
        m.TotalPackages = m.Documents.Sum(d => d.Packages);
        m.UpdatedAt = now;
        m.UpdatedBy = _user.UserId;

        AddEvent(m.Id, ManifestEventTypes.Dispatched, null, ManifestStatuses.Draft, target,
            new { departedAtUtc = m.DepartedAt, documents = m.TotalDocuments });
        if (!withGuides) MarkDocumentsDispatched(m);

        // Recalcula y guarda todo en una sola escritura; si el cálculo falla no se despacha nada.
        CostResult cost;
        try
        {
            cost = await _cost.RecalculateAsync(m);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(ConcurrentChangeMessage);
        }
        await SyncDispatchGroupsAsync(GroupIdsOf(m));
        if (cost.Status == CostStatuses.NoRate)
            warnings.Add("Sin tarifa para este viaje: se despacha igual, pero deberá corregir las comunas/tarifas o ingresar un costo manual con motivo antes del cierre mensual.");
        else if (cost.Status == CostStatuses.Ambiguous)
            warnings.Add("Tarifa ambigua para este viaje: revise el tarifario antes del cierre mensual.");
        if (!withGuides) return new DispatchResult(true, Array.Empty<string>(), warnings);

        return await CompleteGuidesAsync(m, await _guides!.EmitAsync(m.Id), warnings);
    }

    public async Task<DispatchResult> RetryGuidesAsync(Guid manifestId)
    {
        var m = await LoadAsync(manifestId);
        RequireStatus(m, ManifestStatuses.PendingGuides, "Solo se reintentan las guías de un manifiesto con guías pendientes.");
        if (_guides is null) throw new InvalidOperationException("La emisión de guías no está disponible.");
        // Toma el manifiesto (token de concurrencia) antes de emitir: dos reintentos simultáneos no corren a la vez.
        m.Version++;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(ConcurrentChangeMessage);
        }
        return await CompleteGuidesAsync(m, await _guides.EmitAsync(m.Id), new List<string>());
    }

    private const string ConcurrentChangeMessage =
        "Otro usuario modificó este manifiesto al mismo tiempo (despacho o reintento de guías): recargue la página y revise su estado.";

    /// <summary>Con todas las guías emitidas el manifiesto pasa a despachado; si no, sigue en guías pendientes.</summary>
    private async Task<DispatchResult> CompleteGuidesAsync(Manifest m, GuideEmissionOutcome outcome, List<string> warnings)
    {
        if (!outcome.AllIssued)
        {
            warnings.Add("El camión no debe salir sin guía; reintente desde la pestaña Guías.");
            return new DispatchResult(false, outcome.Errors.Count > 0 ? outcome.Errors : new[] { "Hay guías sin emitir." }, warnings);
        }
        var from = m.Status;
        m.Status = ManifestStatuses.Dispatched;
        m.Version++;
        m.UpdatedAt = UtcNow;
        m.UpdatedBy = _user.UserId;
        AddEvent(m.Id, ManifestEventTypes.Dispatched, null, from, ManifestStatuses.Dispatched, new { guides = outcome.Issued });
        MarkDocumentsDispatched(m);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(ConcurrentChangeMessage);
        }
        await SyncDispatchGroupsAsync(GroupIdsOf(m));
        return new DispatchResult(true, Array.Empty<string>(), warnings);
    }

    private void MarkDocumentsDispatched(Manifest m)
    {
        foreach (var d in m.Documents)
        {
            d.DeliveryStatus = DeliveryStatuses.Dispatched;
            AddEvent(m.Id, ManifestEventTypes.Dispatched, d.Id, DeliveryStatuses.Pending, DeliveryStatuses.Dispatched, null);
        }
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

        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == vehicleId && !x.IsTrailer && x.IsActive)
            ?? throw new InvalidOperationException("El vehículo seleccionado no existe, es un carro o está inactivo.");
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

    // ---------------------------------------------------------------- Retorno, anulación, costo, cierre

    private static readonly string[] ReturnStatuses =
    {
        DeliveryStatuses.Delivered, DeliveryStatuses.PartiallyDelivered, DeliveryStatuses.Rejected, DeliveryStatuses.NotDelivered,
    };

    private sealed record PendingNotice(ManifestDocument Doc, string Status, string? ReasonName, string? Comments);

    public async Task RegisterReturnAsync(Guid manifestId, IReadOnlyList<DocumentReturnInput> documents, int? odometerEnd)
    {
        var m = await LoadAsync(manifestId);
        if (m.Status != ManifestStatuses.Dispatched && m.Status != ManifestStatuses.Returned)
            throw new InvalidOperationException("Solo se puede registrar el retorno de un manifiesto despachado (o retornado, para corregirlo antes de cerrar).");
        if (documents.Count == 0 && odometerEnd == null)
            throw new InvalidOperationException("Indique el resultado de al menos un documento.");

        // Validación completa antes de tocar nada.
        var reasons = await _db.RejectionReasons.Where(r => r.CompanyId == CompanyId).ToDictionaryAsync(r => r.Id);
        var items = new List<(ManifestDocument Doc, DocumentReturnInput Input, RejectionReason? Reason)>();
        foreach (var input in documents)
        {
            var doc = m.Documents.FirstOrDefault(d => d.Id == input.ManifestDocumentId)
                ?? throw new InvalidOperationException("El documento no pertenece a este manifiesto.");
            if (!ReturnStatuses.Contains(input.DeliveryStatus))
                throw new InvalidOperationException($"Estado de entrega no válido para el folio {doc.Folio}.");
            RejectionReason? reason = null;
            if (input.DeliveryStatus != DeliveryStatuses.Delivered)
            {
                if (input.RejectionReasonId is not { } rid)
                    throw new InvalidOperationException($"Indique el motivo para el folio {doc.Folio}.");
                if (!reasons.TryGetValue(rid, out reason) || !reason.IsActive)
                    throw new InvalidOperationException($"El motivo seleccionado para el folio {doc.Folio} no existe o está inactivo.");
                var notDelivered = input.DeliveryStatus == DeliveryStatuses.NotDelivered;
                var expected = notDelivered ? RejectionAppliesTo.NotDelivered : RejectionAppliesTo.Rejection;
                if (reason.AppliesTo != expected)
                    throw new InvalidOperationException(
                        $"El motivo '{reason.Name}' no aplica a {(notDelivered ? "no entregado" : "rechazo / entrega parcial")} (folio {doc.Folio}).");
            }
            items.Add((doc, input, reason));
        }
        if (odometerEnd != null)
        {
            if (m.OdometerStart == null)
                throw new InvalidOperationException("El manifiesto no tiene odómetro de inicio; no se puede registrar el odómetro final.");
            if (odometerEnd < m.OdometerStart)
                throw new InvalidOperationException("El odómetro final no puede ser menor que el de inicio.");
        }

        var now = UtcNow;
        var notices = new List<PendingNotice>();
        foreach (var (doc, input, reason) in items)
        {
            var before = doc.DeliveryStatus;
            doc.DeliveryStatus = input.DeliveryStatus;
            doc.RejectionReasonId = input.DeliveryStatus == DeliveryStatuses.Delivered ? null : input.RejectionReasonId;
            doc.Comments = string.IsNullOrWhiteSpace(input.Comments) ? null : input.Comments.Trim();
            doc.ReturnedBy = _user.UserId;
            doc.ReturnedAt = now;
            AddEvent(m.Id, ManifestEventTypes.ReturnRegistered, doc.Id, before, doc.DeliveryStatus,
                new { reason = reason?.Name, comments = doc.Comments });
            if (before != doc.DeliveryStatus
                && (doc.DeliveryStatus == DeliveryStatuses.Rejected || doc.DeliveryStatus == DeliveryStatuses.PartiallyDelivered))
                notices.Add(new PendingNotice(doc, doc.DeliveryStatus, reason?.Name, doc.Comments));
        }

        if (m.Status == ManifestStatuses.Dispatched
            && m.Documents.All(d => ReturnStatuses.Contains(d.DeliveryStatus)))
        {
            m.Status = ManifestStatuses.Returned;
            m.ReturnedAt = now;
            AddEvent(m.Id, ManifestEventTypes.ReturnRegistered, null, ManifestStatuses.Dispatched, ManifestStatuses.Returned, null);
        }
        m.UpdatedAt = now;
        m.UpdatedBy = _user.UserId;

        if (odometerEnd != null)
        {
            m.OdometerEnd = odometerEnd;
            await _cost.RecalculateAsync(m); // guarda todo en una sola escritura
        }
        else
        {
            await _db.SaveChangesAsync();
        }

        await NotifyAsync(m, notices);
    }

    /// <summary>Un aviso por destinatario y documento; se envía con todo ya guardado y nunca deshace el retorno.</summary>
    private async Task NotifyAsync(Manifest m, List<PendingNotice> notices)
    {
        if (notices.Count == 0) return;
        try
        {
            var recipients = await _master.ListNotificationRecipientsAsync();
            var url = $"/despachos/manifiestos/{m.Id:D}";
            foreach (var n in notices)
            {
                var rejected = n.Status == DeliveryStatuses.Rejected;
                var title = rejected ? $"Rechazo en manifiesto N° {m.Number}" : $"Entrega parcial en manifiesto N° {m.Number}";
                var message = $"Manifiesto N° {m.Number}: {(rejected ? "rechazo" : "entrega parcial")} del folio {n.Doc.Folio} "
                    + $"({n.Doc.CustomerName}). Motivo: {n.ReasonName ?? "—"}."
                    + (string.IsNullOrWhiteSpace(n.Comments) ? "" : $" Comentario: {n.Comments}");
                foreach (var r in recipients)
                {
                    try { await _notifications.TryEnqueueAsync(r.UserId, title, message, NotificationSeverity.Warning, url); }
                    catch (Exception ex)
                    {
                        // Un destinatario con problemas no debe impedir el aviso a los demás.
                        _logger?.LogWarning(ex, "No se pudo avisar al usuario {UserId} del manifiesto N° {Number}.", r.UserId, m.Number);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // El aviso es accesorio: el retorno ya quedó guardado.
            _logger?.LogWarning(ex, "No se pudieron enviar los avisos del manifiesto N° {Number}.", m.Number);
        }
    }

    public async Task AnnulAsync(Guid manifestId, string reason)
    {
        var m = await LoadAsync(manifestId);
        if (m.Status != ManifestStatuses.Draft && m.Status != ManifestStatuses.Dispatched && m.Status != ManifestStatuses.PendingGuides)
            throw new InvalidOperationException("Solo se puede anular un manifiesto en borrador, con guías pendientes o despachado.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Indique el motivo de la anulación.");
        var from = m.Status;
        m.Status = ManifestStatuses.Annulled;
        m.Version++;
        // Solo si ninguna guía quedó emitida (lanza en ese caso, sin guardar nada); libera los folios reservados.
        // Su guardado incluye ya el estado anulado del manifiesto: no queda un manifiesto vivo con guías abandonadas.
        if (from == ManifestStatuses.PendingGuides && _guides != null)
            await _guides.AbandonPendingAsync(m.Id, reason.Trim());
        m.UpdatedAt = UtcNow;
        m.UpdatedBy = _user.UserId;
        // Los documentos se conservan como historial; quedan libres porque el manifiesto está anulado.
        AddEvent(m.Id, ManifestEventTypes.Annulled, null, from, ManifestStatuses.Annulled, new { reason = reason.Trim() });
        await _db.SaveChangesAsync();
        // Sus agrupaciones vuelven a "armada" (o siguen despachadas si otro manifiesto vigente las lleva).
        await SyncDispatchGroupsAsync(GroupIdsOf(m));
    }

    public async Task OverrideCostAsync(Guid manifestId, decimal amount, string reason)
    {
        var m = await LoadAsync(manifestId);
        if (m.Status != ManifestStatuses.Dispatched && m.Status != ManifestStatuses.Returned && m.Status != ManifestStatuses.Closed)
            throw new InvalidOperationException("Solo se puede ajustar el costo de un manifiesto despachado, retornado o cerrado.");
        if (amount < 0)
            throw new InvalidOperationException("El costo no puede ser negativo.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Indique el motivo del ajuste de costo.");
        var before = new { calculated = m.CostCalculated, @override = m.CostOverride, final = m.CostFinal };
        m.CostOverride = amount;
        m.CostOverrideReason = reason.Trim();
        m.CostFinal = amount;
        m.UpdatedAt = UtcNow;
        m.UpdatedBy = _user.UserId;
        AddEvent(m.Id, ManifestEventTypes.CostOverridden, null, m.Status, m.Status,
            new { reason = m.CostOverrideReason, before, after = new { calculated = m.CostCalculated, @override = m.CostOverride, final = m.CostFinal } });
        await _db.SaveChangesAsync();
    }

    public async Task CloseAsync(Guid manifestId)
    {
        var m = await LoadAsync(manifestId);
        RequireStatus(m, ManifestStatuses.Returned, "Solo se puede cerrar un manifiesto con el retorno registrado.");
        if (m.CostStatus != CostStatuses.Ok && m.CostOverride == null)
            throw new InvalidOperationException(
                "No se puede cerrar: el manifiesto no tiene costo. Corrija la tarifa/comuna o ingrese un costo manual.");
        m.Status = ManifestStatuses.Closed;
        m.UpdatedAt = UtcNow;
        m.UpdatedBy = _user.UserId;
        AddEvent(m.Id, ManifestEventTypes.Closed, null, ManifestStatuses.Returned, ManifestStatuses.Closed, null);
        await _db.SaveChangesAsync();
    }

    public async Task<CostResult> RecalculateCostAsync(Guid manifestId)
    {
        var m = await LoadAsync(manifestId);
        if (m.Status != ManifestStatuses.Dispatched && m.Status != ManifestStatuses.Returned && m.Status != ManifestStatuses.Closed)
            throw new InvalidOperationException(
                "Solo se puede recalcular el costo de un manifiesto despachado, retornado o cerrado (el borrador se calcula al despachar).");
        return await RecalculateWithEventAsync(m);
    }

    public async Task SetDocumentCommuneByNameAsync(Guid manifestId, Guid manifestDocumentId, string communeName)
    {
        var commune = string.IsNullOrWhiteSpace(communeName) ? null : await _master.FindCommuneByNameAsync(communeName)
            ?? throw new InvalidOperationException($"La comuna '{communeName.Trim()}' no existe en el catálogo. Elija una comuna de la lista.");
        if (commune is null) throw new InvalidOperationException("Escriba la comuna. Elija una comuna de la lista.");
        await SetDocumentCommuneAsync(manifestId, manifestDocumentId, commune.Id);
    }

    public async Task SetDocumentCommuneAsync(Guid manifestId, Guid manifestDocumentId, long communeId)
    {
        var m = await LoadAsync(manifestId);
        if (m.Status != ManifestStatuses.Draft && m.Status != ManifestStatuses.PendingGuides
            && m.Status != ManifestStatuses.Dispatched && m.Status != ManifestStatuses.Returned)
            throw new InvalidOperationException("Solo se puede corregir la comuna de un manifiesto en borrador, con guías pendientes, despachado o retornado.");
        var doc = m.Documents.FirstOrDefault(d => d.Id == manifestDocumentId)
            ?? throw new InvalidOperationException("El documento no está en este manifiesto.");
        var commune = await _db.Communes.FirstOrDefaultAsync(c => c.CompanyId == CompanyId && c.Id == communeId)
            ?? throw new InvalidOperationException("La comuna seleccionada no existe.");

        // La corrección se recuerda como equivalencia del texto de SAP: las próximas facturas con ese texto entran
        // resueltas, y las de este manifiesto que siguen sin resolver con el mismo texto se corrigen ahora.
        await _master.RememberCommuneAliasAsync(doc.DestinationCommuneName, commune.Id);
        var sameText = CommuneNames.Normalize(doc.DestinationCommuneName);
        var targets = m.Documents.Where(d => d.Id == doc.Id
            || (sameText.Length > 0 && d.DestinationCommuneId == null && CommuneNames.Normalize(d.DestinationCommuneName) == sameText));
        foreach (var d in targets.ToList())
        {
            var before = d.DestinationCommuneId;
            d.DestinationCommuneId = commune.Id;
            AddEvent(m.Id, ManifestEventTypes.DocumentCommuneChanged, d.Id, null, null,
                new { folio = d.Folio, sapCommune = d.DestinationCommuneName, beforeCommuneId = before, afterCommuneId = commune.Id, commune = commune.Name });
        }
        m.UpdatedAt = UtcNow;
        m.UpdatedBy = _user.UserId;
        if (m.Status == ManifestStatuses.Draft) await _db.SaveChangesAsync();
        else await RecalculateWithEventAsync(m);
    }

    public const int MaxExtraChargeQuantity = 999;

    public async Task<IReadOnlyList<ManifestExtraChargeRow>> ListExtraChargesAsync(Guid manifestId)
    {
        var m = await _db.Manifests.FirstOrDefaultAsync(x => x.Id == manifestId && x.CompanyId == CompanyId)
                ?? throw new InvalidOperationException("El manifiesto no existe.");
        return await LoadExtraChargeRowsAsync(m);
    }

    public async Task SetExtraChargesAsync(Guid manifestId, IReadOnlyDictionary<long, int> quantities)
    {
        var m = await LoadAsync(manifestId);
        if (m.Status == ManifestStatuses.Annulled)
            throw new InvalidOperationException("No se pueden cambiar los adicionales de un manifiesto anulado.");
        if (quantities.Values.Any(q => q < 0 || q > MaxExtraChargeQuantity))
            throw new InvalidOperationException($"La cantidad de cada adicional debe estar entre 0 y {MaxExtraChargeQuantity}.");

        var rows = await LoadExtraChargeRowsAsync(m);
        var allowed = rows.ToDictionary(r => r.RateSurchargeId);
        if (quantities.Any(q => q.Value > 0 && !allowed.ContainsKey(q.Key)))
            throw new InvalidOperationException("Un adicional seleccionado no existe o no aplica al transportista de este manifiesto.");

        var stored = await _db.ManifestExtraCharges
            .Where(x => x.CompanyId == CompanyId && x.ManifestId == m.Id).ToListAsync();
        var changed = false;
        foreach (var (surchargeId, quantity) in quantities)
        {
            var row = stored.FirstOrDefault(x => x.RateSurchargeId == surchargeId);
            if ((row?.Quantity ?? 0) == quantity) continue;
            changed = true;
            if (quantity == 0) _db.ManifestExtraCharges.Remove(row!);
            else if (row != null) { row.Quantity = quantity; row.UpdatedAt = UtcNow; row.UpdatedBy = _user.UserId; }
            else _db.ManifestExtraCharges.Add(new ManifestExtraCharge
            {
                CompanyId = CompanyId, ManifestId = m.Id, RateSurchargeId = surchargeId, Quantity = quantity,
                UpdatedAt = UtcNow, UpdatedBy = _user.UserId,
            });
        }
        if (!changed) return;

        var after = rows.Select(r => new
        {
            name = r.Name,
            before = r.Quantity,
            after = quantities.TryGetValue(r.RateSurchargeId, out var q) ? q : r.Quantity,
        }).Where(x => x.before != x.after).ToList();
        m.UpdatedAt = UtcNow;
        m.UpdatedBy = _user.UserId;
        AddEvent(m.Id, ManifestEventTypes.ExtraChargesChanged, null, m.Status, m.Status, new { changes = after });
        // Se guardan antes de recalcular: el costo lee las cantidades desde la base. En borrador el costo se calcula al despachar.
        await _db.SaveChangesAsync();
        if (m.Status != ManifestStatuses.Draft) await RecalculateWithEventAsync(m);
    }

    private async Task<IReadOnlyList<ManifestExtraChargeRow>> LoadExtraChargeRowsAsync(Manifest m)
    {
        var stored = await _db.ManifestExtraCharges
            .Where(x => x.CompanyId == CompanyId && x.ManifestId == m.Id)
            .ToDictionaryAsync(x => x.RateSurchargeId, x => x.Quantity);
        var storedIds = stored.Keys.ToList();
        var date = m.DispatchDate;
        var surcharges = await _db.RateSurcharges
            .Where(s => s.CompanyId == CompanyId && s.ConditionType == ManualQuantityCondition.Code
                        && (storedIds.Contains(s.Id)
                            || (s.IsActive && (s.CarrierId == null || s.CarrierId == m.CarrierId)
                                && s.ValidFrom <= date && (s.ValidTo == null || s.ValidTo >= date))))
            .OrderBy(s => s.Priority).ThenBy(s => s.Name)
            .ToListAsync();
        return surcharges
            .Select(s => new ManifestExtraChargeRow(s.Id, s.Name, s.EffectType, s.Value, stored.GetValueOrDefault(s.Id)))
            .ToList();
    }

    /// <summary>Recalcula y deja el evento cost_recalculated (antes/después); todo se guarda junto.</summary>
    private async Task<CostResult> RecalculateWithEventAsync(Manifest m)
    {
        var before = new { status = m.CostStatus, calculated = m.CostCalculated, final = m.CostFinal };
        var result = await _cost.RecalculateAsync(m); // guarda también los eventos pendientes
        AddEvent(m.Id, ManifestEventTypes.CostRecalculated, null, m.Status, m.Status,
            new { before, after = new { status = m.CostStatus, calculated = m.CostCalculated, final = m.CostFinal } });
        await _db.SaveChangesAsync();
        return result;
    }

    // ---------------------------------------------------------------- Consulta

    public async Task<Manifest?> GetAsync(Guid manifestId) =>
        await _db.Manifests
            .Include(x => x.Documents).Include(x => x.CostLines).Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == manifestId && x.CompanyId == CompanyId);

    public async Task<Manifest?> GetByNumberAsync(int number) =>
        await _db.Manifests
            .Include(x => x.Documents).Include(x => x.CostLines).Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Number == number && x.CompanyId == CompanyId);

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

    /// <summary>Carga el manifiesto para mutarlo: si su dispatch_date cae en un período cerrado, no se toca.</summary>
    private async Task<Manifest> LoadAsync(Guid id)
    {
        var m = await _db.Manifests.Include(x => x.Documents)
                    .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId)
                ?? throw new InvalidOperationException("El manifiesto no existe.");
        await RequireOpenPeriodAsync(m.DispatchDate);
        return m;
    }

    private async Task RequireOpenPeriodAsync(DateOnly date)
    {
        if (await _closure.IsClosedAsync(date))
            throw new InvalidOperationException(MonthlyClosureService.ClosedPeriodMessage(date));
    }

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
