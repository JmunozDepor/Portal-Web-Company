using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modulo.Despachos.Data;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Dominio.Guias;
using Modulo.Despachos.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.Despachos.Servicios;

/// <summary>
/// Guías de despacho del manifiesto (ver ITransportGuideService). El plan se persiste en transport_guides la
/// primera vez que se emite; folio, XML e idempotencia viven en la pasarela DTE (EmissionKey por guía).
/// </summary>
public sealed class TransportGuideService : ITransportGuideService
{
    public const string SourceModule = "despachos";
    private const int GuideDteType = 52;
    private static readonly JsonSerializerOptions Json = new();

    private readonly DespachosDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly ISapDocumentLookupService _sap;
    private readonly IDteEmissionService _dte;
    private readonly TimeProvider _time;
    private readonly INotificationService _notifications;
    private readonly ILogger<TransportGuideService>? _logger;

    private readonly ISapTransferWriter? _writer;

    public TransportGuideService(
        DespachosDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user, ISapDocumentLookupService sap,
        IDteEmissionService dte, TimeProvider time, INotificationService notifications, ILogger<TransportGuideService>? logger = null,
        ISapTransferWriter? writer = null)
    {
        _writer = writer;
        _db = db;
        _company = company;
        _user = user;
        _sap = sap;
        _dte = dte;
        _time = time;
        _notifications = notifications;
        _logger = logger;
    }

    private Guid CompanyId => _company.CompanyId;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public async Task<bool> IsEnabledAsync() =>
        await _db.Settings.AnyAsync(s => s.CompanyId == CompanyId && s.GuidesEnabled);

    public async Task<IReadOnlyList<TransportGuide>> ListAsync(Guid manifestId) =>
        await _db.TransportGuides.AsNoTracking().Include(g => g.Documents)
            .Where(g => g.CompanyId == CompanyId && g.ManifestId == manifestId)
            .OrderBy(g => g.Sequence).ToListAsync();

    // ---------------------------------------------------------------- Vista previa

    public async Task<GuidePreview> PreviewAsync(Guid manifestId)
    {
        var m = await LoadReadOnlyAsync(manifestId);
        var settings = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == CompanyId);
        var manual = await ManualGuidesOfAsync(m, tracking: false);
        var res154 = WantsRes154(m, settings);
        if (!res154 && manual.Count == 0)
            return new GuidePreview(false, Array.Empty<PlannedGuideView>(), Array.Empty<string>(), Array.Empty<string>());

        var draft = m.Status == ManifestStatuses.Draft;
        var views = new List<PlannedGuideView>();
        var errors = new List<string>();
        IReadOnlyList<string> warnings = Array.Empty<string>();
        if (res154)
        {
            var (plan, planErrors) = await PlanAsync(m, settings!, previewSnapshot: draft);
            views.AddRange(plan?.Guides.Select(g => View(g, m)) ?? Enumerable.Empty<PlannedGuideView>());
            errors.AddRange(planErrors);
            warnings = plan?.Warnings ?? Array.Empty<string>();
        }
        else
        {
            await AddConfigurationErrorsAsync(settings, errors);
            if (draft) await ApplyCurrentMastersAsync(m, errors);
        }
        errors.AddRange(await ValidateManualAsync(m, manual, arrivalCheckedByPlanner: res154));
        views.AddRange(manual.Select(g => new PlannedGuideView(0, g.ReceiverTaxId, g.ReceiverName, ManualGuideService.Code(g),
            $"{g.DestinationAddress}, {g.DestinationCommune}", 1, g.LineCount, g.TotalQuantity, null)));
        return new GuidePreview(true, views, errors, warnings);
    }

    /// <summary>Guías Res. 154 a planificar: emisión activada y facturas o transferencias en el manifiesto.</summary>
    private static bool WantsRes154(Manifest m, DespachosSetting? settings) =>
        settings is { GuidesEnabled: true } && m.Documents.Any(d => d.DocumentKind is DocumentKinds.Invoice or DocumentKinds.Transfer);

    /// <summary>Guías manuales que viajan en el manifiesto (documentos manual_guide), por número.</summary>
    private async Task<List<TransportGuide>> ManualGuidesOfAsync(Manifest m, bool tracking)
    {
        var ids = m.Documents.Where(d => d.DocumentKind == DocumentKinds.ManualGuide && d.TransportGuideId != null)
            .Select(d => d.TransportGuideId!.Value).ToList();
        if (ids.Count == 0) return new List<TransportGuide>();
        var q = _db.TransportGuides.Where(g => g.CompanyId == CompanyId && ids.Contains(g.Id));
        if (!tracking) q = q.AsNoTracking();
        return await q.OrderBy(g => g.Number).ToListAsync();
    }

    /// <summary>
    /// Documento a emitir de una guía manual del manifiesto: su contenido con el transporte y el origen del manifiesto
    /// (snapshot) y la fecha de salida.
    /// </summary>
    private DteDocument ManualDocument(Manifest m, TransportGuide g)
    {
        var departure = ChileClock.ToLocal(m.DepartedAt ?? UtcNow);
        var date = DateOnly.FromDateTime(departure);
        var transport = new DteTransport(
            m.Plate, m.TrailerPlate, m.CarrierTaxId,
            m.DriverTaxId is null && m.DriverName is null ? null : new DteDriver(m.DriverTaxId ?? "", m.DriverName ?? ""),
            null, null, null, null, date, new TimeOnly(departure.Hour, departure.Minute), m.EstimatedArrivalDate);
        var draft = JsonSerializer.Deserialize<DteDocument>(g.DocumentJson, Json)!;
        var doc = ManualGuideBuilder.ForEmission(draft, g.Notes, transport, date);
        return doc with { Issuer = new DteIssuerOverride(m.OriginAddress, m.OriginCommune, null) };
    }

    /// <summary>Reglas de la Res. 154 y del formato para las guías manuales del manifiesto (sin red).</summary>
    private async Task<List<string>> ValidateManualAsync(Manifest m, IReadOnlyList<TransportGuide> manual, bool arrivalCheckedByPlanner)
    {
        var errors = new List<string>();
        if (manual.Count == 0) return errors;
        if (m.EstimatedArrivalDate is null && !arrivalCheckedByPlanner)
            errors.Add("Ingrese la fecha de llegada estimada (obligatoria en la guía de despacho, Res. 154).");
        foreach (var g in manual.Where(g => g.Status != TransportGuideStatuses.Issued))
            foreach (var e in await _dte.ValidateAsync(ManualDocument(m, g)))
                errors.Add($"Guía manual {ManualGuideService.Code(g)}: {e.Message}");
        return errors;
    }

    /// <summary>Configuración DTE de la compañía (cuando no hay guías Res. 154 que ya la validen).</summary>
    private async Task AddConfigurationErrorsAsync(DespachosSetting? settings, List<string> errors)
    {
        var config = await _dte.GetConfigurationAsync();
        if (!config.IsConfigured) errors.Add(config.Error ?? "La compañía no tiene configurados los documentos electrónicos.");
        else if (!config.SupportedTypes.Contains(GuideDteType))
            errors.Add($"El proveedor {config.ProviderCode} no está habilitado para guías de despacho (52).");
    }

    public async Task<byte[]> PreviewPdfAsync(Guid manifestId, int sequence)
    {
        var m = await LoadReadOnlyAsync(manifestId);
        var settings = await RequireSettingsAsync();
        var (plan, errors) = await PlanAsync(m, settings, previewSnapshot: m.Status == ManifestStatuses.Draft);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        var guide = plan!.Guides.FirstOrDefault(g => g.Sequence == sequence)
            ?? throw new InvalidOperationException($"La guía {sequence} no existe en el plan.");
        var preview = await _dte.PreviewAsync(guide.Document);
        return preview.Pdf ?? throw new InvalidOperationException(
            "No hay vista previa: " + string.Join("; ", preview.Errors.Select(e => e.Message)));
    }

    // ---------------------------------------------------------------- Emisión

    public async Task<GuideEmissionOutcome> EmitAsync(Guid manifestId)
    {
        var m = await _db.Manifests.Include(x => x.Documents)
                    .FirstOrDefaultAsync(x => x.Id == manifestId && x.CompanyId == CompanyId)
                ?? throw new InvalidOperationException("El manifiesto no existe.");
        if (m.Status != ManifestStatuses.PendingGuides)
            throw new InvalidOperationException("Solo se emiten guías de un manifiesto con guías pendientes.");

        var guides = await _db.TransportGuides.Include(g => g.Documents)
            .Where(g => g.CompanyId == CompanyId && g.ManifestId == m.Id).OrderBy(g => g.Sequence).ToListAsync();
        if (guides.Any(g => g.Status == TransportGuideStatuses.Abandoned))
            return new GuideEmissionOutcome(false, 0, 0, new[] { "Las guías de este manifiesto fueron anuladas: no se pueden emitir." });
        var manual = await ManualGuidesOfAsync(m, tracking: true);
        // Con guías ya planificadas se sigue aunque luego se desactive la emisión (si no, el manifiesto queda atascado).
        var storedSettings = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == CompanyId);
        var res154 = guides.Count > 0 || WantsRes154(m, storedSettings);
        if (!res154 && manual.Count == 0) await RequireSettingsAsync();
        var settings = storedSettings ?? new DespachosSetting();
        var errors = new List<string>();
        if (res154)
            errors.AddRange(await EmitRes154Async(m, guides, settings));
        errors.AddRange(await EmitManualAsync(m, manual));

        var all = guides.Count + manual.Count;
        var issued = guides.Count(g => g.Status == TransportGuideStatuses.Issued) + manual.Count(g => g.Status == TransportGuideStatuses.Issued);
        var pending = all - issued;
        if (pending > 0)
        {
            AddEvent(m, ManifestEventTypes.GuideFailed, new { issued, pending, errors });
            await _db.SaveChangesAsync();
            await NotifyFailureAsync(m, pending, errors);
        }
        // "Todas emitidas" exige al menos una guía y todas emitidas: nunca se despacha con cero guías.
        return new GuideEmissionOutcome(all > 0 && pending == 0, issued, pending, errors);
    }

    /// <summary>
    /// Guías manuales del manifiesto: se emiten con su llave estable (la pasarela devuelve la emisión existente si ya
    /// salió) y, emitidas, dejan su folio en el documento del manifiesto.
    /// </summary>
    private async Task<List<string>> EmitManualAsync(Manifest m, List<TransportGuide> manual)
    {
        var errors = new List<string>();
        foreach (var g in manual.Where(g => g.Status != TransportGuideStatuses.Issued))
        {
            var code = ManualGuideService.Code(g);
            try
            {
                var r = await _dte.EmitAsync(new DteEmissionRequest(g.EmissionKey, SourceModule, $"manifest:{m.Number}/manual:{code}",
                    ManualDocument(m, g)));
                ManualGuideService.Apply(g, r, UtcNow, _user.UserId);
            }
            catch (InvalidOperationException ex)
            {
                g.Status = TransportGuideStatuses.Failed;
                g.LastError = Truncate(ex.Message);
            }
            if (g.Status == TransportGuideStatuses.Issued)
            {
                g.IssueDate = DateOnly.FromDateTime(ChileClock.ToLocal(m.DepartedAt ?? UtcNow));
                foreach (var d in m.Documents.Where(d => d.TransportGuideId == g.Id)) d.Folio = g.Folio ?? 0;
                AddEvent(m, ManifestEventTypes.GuideIssued, new { manual = code, folio = g.Folio });
            }
            else
                errors.Add($"Guía manual {code} ({g.ReceiverName}, {g.DestinationCommune}): {g.LastError ?? "sin respuesta del proveedor"}");
            await _db.SaveChangesAsync();
        }
        return errors;
    }

    /// <summary>Guías Res. 154 planificadas desde las facturas y transferencias del manifiesto (Fase A/B).</summary>
    private async Task<List<string>> EmitRes154Async(Manifest m, List<TransportGuide> guides, DespachosSetting settings)
    {

        // Estado real en la pasarela: una guía con emisión en curso o emitida nunca se replanifica (mismo contenido).
        var locked = new HashSet<Guid>();
        foreach (var g in guides.Where(g => g.Status != TransportGuideStatuses.Issued))
        {
            var gw = await _dte.GetAsync(g.EmissionKey);
            if (gw is null || (gw.Status == DteEmissionStatuses.Failed && gw.Folio is null)) continue;
            locked.Add(g.Id);
            Apply(g, gw);
        }

        // Plan vigente (con el snapshot ya tomado): define las guías la primera vez y reemplaza las rechazadas.
        var (plan, planErrors) = await PlanAsync(m, settings, previewSnapshot: false);
        if (guides.Count == 0)
        {
            if (plan is null || planErrors.Count > 0) return planErrors.Count > 0 ? planErrors.ToList() : new List<string> { "No se pudo planificar las guías." };
            guides.AddRange(Persist(m, settings, plan));
            AddEvent(m, ManifestEventTypes.GuidesPlanned, new { guides = guides.Count });
            await _db.SaveChangesAsync();
        }
        else
        {
            if (plan is not null && planErrors.Count == 0) Replan(guides.Where(g => !locked.Contains(g.Id)).ToList(), plan);
            await _db.SaveChangesAsync();
        }

        var errors = new List<string>();
        foreach (var g in guides.Where(g => g.Status is not (TransportGuideStatuses.Issued or TransportGuideStatuses.Abandoned)))
        {
            var doc = JsonSerializer.Deserialize<DteDocument>(g.DocumentJson, Json)!;
            try
            {
                var r = await _dte.EmitAsync(new DteEmissionRequest(g.EmissionKey, SourceModule, $"manifest:{m.Number}/guide:{g.Sequence}", doc));
                Apply(g, r);
            }
            catch (InvalidOperationException ex)
            {
                g.Status = TransportGuideStatuses.Failed;
                g.LastError = Truncate(ex.Message);
            }
            if (g.Status == TransportGuideStatuses.Issued)
                AddEvent(m, ManifestEventTypes.GuideIssued, new { sequence = g.Sequence, folio = g.Folio });
            else
                errors.Add($"Guía {g.Sequence} ({g.ReceiverName}, {g.DestinationCommune}): {g.LastError ?? "sin respuesta del proveedor"}");
            await _db.SaveChangesAsync();
        }

        // Folio en las transferencias SAP de toda guía emitida: las de este envío y las que la pasarela emitió
        // entretanto (reconciliadas arriba). Idempotente: no reescribe las que ya quedaron escritas.
        foreach (var g in guides.Where(g => g.Status == TransportGuideStatuses.Issued))
            await WriteBackAsync(m, g, settings);
        return errors;
    }

    public async Task<byte[]> GetPdfAsync(Guid manifestId, int? sequence)
    {
        if (!await _db.Manifests.AnyAsync(x => x.Id == manifestId && x.CompanyId == CompanyId))
            throw new InvalidOperationException("El manifiesto no existe.");
        var guides = await _db.TransportGuides.AsNoTracking()
            .Where(g => g.CompanyId == CompanyId && g.ManifestId == manifestId && g.Status == TransportGuideStatuses.Issued
                        && (sequence == null || g.Sequence == sequence))
            .OrderBy(g => g.Sequence).ToListAsync();
        if (sequence is null)
        {
            // Todas: también las guías manuales que viajan en el manifiesto, después de las Res. 154.
            var manualIds = await _db.ManifestDocuments.AsNoTracking()
                .Where(d => d.CompanyId == CompanyId && d.ManifestId == manifestId && d.TransportGuideId != null)
                .Select(d => d.TransportGuideId!.Value).ToListAsync();
            guides.AddRange(await _db.TransportGuides.AsNoTracking()
                .Where(g => g.CompanyId == CompanyId && manualIds.Contains(g.Id) && g.Status == TransportGuideStatuses.Issued)
                .OrderBy(g => g.Number).ToListAsync());
        }
        if (guides.Count == 0) throw new InvalidOperationException("No hay guías emitidas para imprimir.");

        var pdfs = new List<byte[]>();
        foreach (var g in guides) pdfs.Add(await _dte.GetPdfAsync(g.EmissionKey, cedible: false, copies: 1));
        return pdfs.Count == 1 ? pdfs[0] : Merge(pdfs);
    }

    public async Task AbandonPendingAsync(Guid manifestId, string reason)
    {
        var guides = await _db.TransportGuides
            .Where(g => g.CompanyId == CompanyId && g.ManifestId == manifestId).ToListAsync();
        var manualIds = await _db.ManifestDocuments
            .Where(d => d.CompanyId == CompanyId && d.ManifestId == manifestId && d.TransportGuideId != null)
            .Select(d => d.TransportGuideId!.Value).ToListAsync();
        var manual = await _db.TransportGuides.Where(g => g.CompanyId == CompanyId && manualIds.Contains(g.Id)).ToListAsync();
        if (guides.Concat(manual).Any(g => g.Status == TransportGuideStatuses.Issued))
            throw new InvalidOperationException(
                "Hay guías emitidas: no se puede anular el manifiesto; consulte cómo anular la guía (pendiente con FACELE).");
        foreach (var g in guides.Where(g => g.Status != TransportGuideStatuses.Abandoned))
        {
            // Se consulta la pasarela aunque la guía figure "planificada": pudo quedar una emisión a medias.
            if (await _dte.GetAsync(g.EmissionKey) is not null)
                await _dte.AbandonAsync(g.EmissionKey, reason);
            g.Status = TransportGuideStatuses.Abandoned;
            g.LastError = Truncate("Anulada: " + reason);
        }
        // Las guías manuales no se pierden: se abandona su emisión y quedan con llave nueva, listas para volver a
        // borrador (ManifestService las libera al anular).
        foreach (var g in manual)
        {
            if (await _dte.GetAsync(g.EmissionKey) is not null)
                await _dte.AbandonAsync(g.EmissionKey, reason);
            g.EmissionKey = Guid.NewGuid();
            g.Status = TransportGuideStatuses.InManifest;
            g.Folio = null;
            g.LastError = null;
        }
        await _db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- Escritura en SAP (Fase B)

    public async Task<int> RetrySapWritebackAsync(Guid manifestId)
    {
        var m = await _db.Manifests.Include(x => x.Documents)
                    .FirstOrDefaultAsync(x => x.Id == manifestId && x.CompanyId == CompanyId)
                ?? throw new InvalidOperationException("El manifiesto no existe.");
        var settings = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == CompanyId) ?? new DespachosSetting();
        var guides = await _db.TransportGuides.Include(g => g.Documents)
            .Where(g => g.CompanyId == CompanyId && g.ManifestId == m.Id && g.Status == TransportGuideStatuses.Issued)
            .OrderBy(g => g.Sequence).ToListAsync();
        foreach (var g in guides) await WriteBackAsync(m, g, settings);
        return m.Documents.Count(d => d.SapWritebackStatus == SapWritebackStatuses.Pending);
    }

    public async Task<string> TestSapWriteAsync(int docEntry)
    {
        var settings = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == CompanyId) ?? new DespachosSetting();
        var names = WritebackFields(settings, 0, 0, default).Keys.ToList();
        if (names.Count == 0 || _writer is null)
            throw new InvalidOperationException("No hay campos configurados para escribir en SAP (prefijo de folio o campos de usuario).");
        try
        {
            var current = await _writer.ReadAsync(docEntry, names);
            await _writer.WriteAsync(docEntry, current);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"SAP rechazó la escritura en la transferencia {docEntry}: {ex.Message}", ex);
        }
        return $"SAP aceptó la escritura en la transferencia {docEntry}.";
    }

    /// <summary>
    /// Campos que se escriben en la transferencia: FolioNumber + FolioPrefixString si hay prefijo, y los campos de
    /// usuario configurados (N° de manifiesto, fecha de despacho yyyy-MM-dd). Vacío = no se escribe nada.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> WritebackFields(DespachosSetting s, long folio, int manifestNumber, DateOnly dispatchDate)
    {
        var fields = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(s.TransferFolioPrefix))
        {
            fields["FolioNumber"] = folio;
            fields["FolioPrefixString"] = s.TransferFolioPrefix.Trim();
        }
        if (!string.IsNullOrWhiteSpace(s.TransferManifestField)) fields[s.TransferManifestField.Trim()] = manifestNumber;
        if (!string.IsNullOrWhiteSpace(s.TransferDispatchDateField))
            fields[s.TransferDispatchDateField.Trim()] = dispatchDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return fields;
    }

    /// <summary>
    /// Escribe el folio de la guía emitida en sus transferencias aún no escritas. Una falla deja la transferencia
    /// "SAP pendiente" con aviso por la campana: la guía emitida es el documento legal y el despacho sigue.
    /// </summary>
    private async Task WriteBackAsync(Manifest m, TransportGuide g, DespachosSetting settings)
    {
        if (g.Folio is not { } folio || _writer is null) return;
        var ids = g.Documents.Select(x => x.ManifestDocumentId).ToHashSet();
        var docs = m.Documents.Where(d => ids.Contains(d.Id) && d.DocumentKind == DocumentKinds.Transfer
                                          && d.SapWritebackStatus != SapWritebackStatuses.Ok).ToList();
        var fields = WritebackFields(settings, folio, m.Number, m.DispatchDate);
        if (docs.Count == 0 || fields.Count == 0) return;

        var failed = new List<ManifestDocument>();
        foreach (var d in docs)
        {
            try
            {
                await _writer.WriteAsync(d.SapDocEntry, fields);
                d.SapWritebackStatus = SapWritebackStatuses.Ok;
                d.SapWritebackError = null;
            }
            catch (Exception ex)
            {
                d.SapWritebackStatus = SapWritebackStatuses.Pending;
                d.SapWritebackError = ex.Message is { Length: > 500 } e ? e[..500] : ex.Message;
                failed.Add(d);
            }
        }
        if (failed.Count > 0)
            AddEvent(m, ManifestEventTypes.SapWritebackFailed, new { folio, transfers = failed.Select(d => d.Folio), error = failed[0].SapWritebackError });
        await _db.SaveChangesAsync();
        if (failed.Count == 0) return;
        try
        {
            var list = string.Join(", ", failed.Select(d => d.Folio));
            await _notifications.TryEnqueueAsync(_user.UserId, $"SAP pendiente en manifiesto N° {m.Number}",
                $"Manifiesto N° {m.Number}: no se pudo escribir el folio {folio} en la transferencia {list} de SAP. Reintente desde la pestaña Guías.",
                NotificationSeverity.Warning, $"/despachos/manifiestos/{m.Id:D}?tab=guias");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No se pudo avisar la escritura SAP pendiente del manifiesto N° {Number}.", m.Number);
        }
    }

    // ---------------------------------------------------------------- Plan

    private async Task<(GuidePlan? Plan, IReadOnlyList<string> Errors)> PlanAsync(Manifest m, DespachosSetting settings, bool previewSnapshot)
    {
        var errors = new List<string>();
        var config = await _dte.GetConfigurationAsync();
        if (!config.IsConfigured) errors.Add(config.Error ?? "La compañía no tiene configurados los documentos electrónicos.");
        else
        {
            if (!config.SupportedTypes.Contains(GuideDteType))
                errors.Add($"El proveedor {config.ProviderCode} no está habilitado para guías de despacho (52).");
            if (!string.IsNullOrWhiteSpace(settings.IssuerTaxId) && config.IssuerTaxId is { } issuer
                && RutChileno.IsValid(settings.IssuerTaxId) && RutChileno.Normalize(settings.IssuerTaxId) != issuer)
                errors.Add($"El RUT emisor de documentos electrónicos ({issuer}) no coincide con el de Despachos ({settings.IssuerTaxId}); revise la configuración.");
        }
        if (m.Documents.Count == 0) errors.Add("El manifiesto no tiene documentos.");

        if (previewSnapshot) await ApplyCurrentMastersAsync(m, errors);

        var docs = new List<(ManifestDocument, SapDocumentLines)>();
        foreach (var d in m.Documents.OrderBy(d => d.AddedAt))
        {
            var linesKind = d.DocumentKind switch
            {
                DocumentKinds.Invoice => SapQueryKinds.InvoiceLines,
                DocumentKinds.Transfer => SapQueryKinds.TransferLines,
                _ => null, // guía del add-on por folio: ya tiene su guía
            };
            if (linesKind is null)
            {
                docs.Add((d, new SapDocumentLines(null, null, Array.Empty<SapDocumentLine>())));
                continue;
            }
            try
            {
                docs.Add((d, await _sap.GetLinesAsync(linesKind, d.SapDocEntry)));
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"{(d.DocumentKind == DocumentKinds.Transfer ? "Transferencia" : "Folio")} {d.Folio}: {ex.Message}");
            }
        }

        var communeIds = m.Documents.Select(d => d.DestinationCommuneId).Where(i => i != null).Distinct().ToList();
        var communes = await _db.Communes.AsNoTracking()
            .Where(c => c.CompanyId == CompanyId && communeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);

        var departure = ChileClock.ToLocal(m.DepartedAt ?? UtcNow);
        var plan = GuidePlanner.Plan(new GuidePlanInput(
            m, docs, id => id is { } x && communes.TryGetValue(x, out var n) ? n : null,
            settings.TransferIndicator, settings.DispatchType,
            DateOnly.FromDateTime(departure), new TimeOnly(departure.Hour, departure.Minute), settings.GuideLineGrouping,
            settings.GuideItemCodePlacement, settings.GuideMaxLines, settings.GuideValued,
            // Guías de traslado interno (Fase B): el receptor es la propia compañía, con su razón social y giro DTE.
            config.IsConfigured && !string.IsNullOrWhiteSpace(config.IssuerName)
                ? new DteReceiver(config.IssuerTaxId ?? "", config.IssuerName, config.IssuerActivity, null, null, null)
                : null,
            settings.InternalTransferIndicator, settings.InternalDispatchType));
        errors.AddRange(plan.Errors);

        foreach (var g in plan.Guides)
            foreach (var e in await _dte.ValidateAsync(g.Document))
                errors.Add($"Guía {g.Sequence}: {e.Message}");
        return (plan, errors);
    }

    /// <summary>En borrador aún no hay snapshot: se toman los maestros vigentes (sin tocar el manifiesto guardado).</summary>
    private async Task ApplyCurrentMastersAsync(Manifest m, List<string> errors)
    {
        var carrier = await _db.Carriers.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == m.CarrierId);
        var vehicle = await _db.Vehicles.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == m.VehicleId);
        var trailer = m.TrailerId == null ? null
            : await _db.Vehicles.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == m.TrailerId);
        var driver = await _db.Drivers.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == m.DriverId);
        var origin = await _db.Origins.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == m.OriginId);
        if (carrier is null || vehicle is null || driver is null || origin is null)
        {
            errors.Add("Complete transportista, vehículo, chofer y origen del manifiesto.");
            return;
        }
        m.CarrierTaxId = carrier.TaxId;
        m.DriverTaxId = driver.TaxId;
        m.DriverName = driver.FullName;
        m.Plate = vehicle.Plate;
        m.TrailerPlate = trailer?.Plate;
        m.OriginAddress = origin.Address;
        m.OriginCommune = origin.CommuneId == null ? null
            : (await _db.Communes.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == CompanyId && c.Id == origin.CommuneId))?.Name;
    }

    private List<TransportGuide> Persist(Manifest m, DespachosSetting settings, GuidePlan plan)
    {
        var now = UtcNow;
        var list = new List<TransportGuide>();
        foreach (var p in plan.Guides)
        {
            var g = new TransportGuide
            {
                Id = Guid.NewGuid(), CompanyId = CompanyId, ManifestId = m.Id, Sequence = p.Sequence, EmissionKey = Guid.NewGuid(),
                TransferIndicator = p.Document.IdDoc.TransferIndicator ?? settings.TransferIndicator,
                Status = TransportGuideStatuses.Planned, CreatedAt = now,
                Documents = p.ManifestDocumentIds.Select(id => new TransportGuideDocument { CompanyId = CompanyId, ManifestDocumentId = id }).ToList(),
            };
            Fill(g, p);
            _db.TransportGuides.Add(g);
            list.Add(g);
        }
        return list;
    }

    /// <summary>
    /// Reemplaza el contenido de las guías rechazadas (o nunca enviadas) por el plan vigente, siempre que cubran los
    /// mismos documentos. Las en curso o emitidas no se tocan: se reenvían con su contenido original.
    /// </summary>
    private static void Replan(List<TransportGuide> guides, GuidePlan plan)
    {
        foreach (var g in guides.Where(g => g.Status is TransportGuideStatuses.Failed or TransportGuideStatuses.Planned))
        {
            var p = plan.Guides.FirstOrDefault(x => x.Sequence == g.Sequence);
            var sameDocs = p is not null && p.ManifestDocumentIds.OrderBy(i => i)
                .SequenceEqual(g.Documents.Select(d => d.ManifestDocumentId).OrderBy(i => i));
            if (sameDocs) Fill(g, p!);
        }
    }

    private static void Fill(TransportGuide g, PlannedGuide p)
    {
        var d = p.Document;
        g.ReceiverTaxId = d.Receiver.TaxId;
        g.ReceiverName = d.Receiver.Name;
        g.DestinationAddress = d.Receiver.Address ?? "";
        g.DestinationCommune = d.Receiver.Commune ?? "";
        g.DestinationCity = d.Receiver.City;
        g.LineCount = d.Lines.Count;
        g.TotalQuantity = d.Lines.Sum(l => l.Quantity);
        g.WeightKg = d.Transport?.Customs?.NetWeightKg;
        g.DocumentJson = JsonSerializer.Serialize(d, Json);
    }

    private void Apply(TransportGuide g, DteEmissionResult r)
    {
        g.Folio = r.Folio;
        g.LastError = Truncate(r.Error);
        switch (r.Status)
        {
            case DteEmissionStatuses.Issued:
                g.Status = TransportGuideStatuses.Issued;
                g.IssuedAt = UtcNow;
                g.LastError = null;
                break;
            case DteEmissionStatuses.Failed:
                g.Status = TransportGuideStatuses.Failed;
                break;
            case DteEmissionStatuses.Released or DteEmissionStatuses.Abandoned:
                g.Status = TransportGuideStatuses.Abandoned;
                break;
            default:
                g.Status = TransportGuideStatuses.Pending;
                break;
        }
    }

    private static PlannedGuideView View(PlannedGuide g, Manifest m) => new(
        g.Sequence, g.Document.Receiver.TaxId, g.Document.Receiver.Name,
        CustomerCodes(m.Documents.Where(d => g.ManifestDocumentIds.Contains(d.Id))),
        $"{g.Document.Receiver.Address}, {g.Document.Receiver.Commune}",
        g.ManifestDocumentIds.Count, g.Document.Lines.Count, g.Document.Lines.Sum(l => l.Quantity),
        g.Document.Transport?.Customs?.NetWeightKg);

    /// <summary>Códigos de socio de negocio SAP (CardCode) distintos de los documentos, separados por coma.</summary>
    public static string CustomerCodes(IEnumerable<ManifestDocument> docs) =>
        string.Join(", ", docs.Select(d => d.CustomerCode).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct());

    // ---------------------------------------------------------------- Helpers

    private async Task<Manifest> LoadReadOnlyAsync(Guid id) =>
        await _db.Manifests.AsNoTracking().Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId)
        ?? throw new InvalidOperationException("El manifiesto no existe.");

    private async Task<DespachosSetting> RequireSettingsAsync() =>
        await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == CompanyId && s.GuidesEnabled)
        ?? throw new InvalidOperationException("La compañía no tiene activada la emisión de guías de despacho.");

    private async Task NotifyFailureAsync(Manifest m, int pending, IReadOnlyList<string> errors)
    {
        try
        {
            var message = $"Manifiesto N° {m.Number}: {pending} guía(s) sin emitir. {errors.FirstOrDefault() ?? ""} " +
                          "El camión no debe salir sin guía; reintente desde la pestaña Guías.";
            await _notifications.TryEnqueueAsync(_user.UserId, $"Guías pendientes en manifiesto N° {m.Number}",
                message.Trim(), NotificationSeverity.Warning, $"/despachos/manifiestos/{m.Id:D}?tab=guias");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No se pudo avisar las guías pendientes del manifiesto N° {Number}.", m.Number);
        }
    }

    private void AddEvent(Manifest m, string type, object payload) =>
        _db.ManifestEvents.Add(new ManifestEvent
        {
            CompanyId = CompanyId, ManifestId = m.Id, EventType = type, FromStatus = m.Status, ToStatus = m.Status,
            UserId = _user.UserId, OccurredAt = UtcNow, PayloadJson = JsonSerializer.Serialize(payload),
        });

    private static byte[] Merge(IEnumerable<byte[]> pdfs)
    {
        using var output = new PdfDocument();
        foreach (var bytes in pdfs)
        {
            using var input = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
            foreach (var page in input.Pages) output.AddPage(page);
        }
        using var ms = new MemoryStream();
        output.Save(ms);
        return ms.ToArray();
    }

    private static string? Truncate(string? s) => s is { Length: > 1000 } ? s[..1000] : s;
}
