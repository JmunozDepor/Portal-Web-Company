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

    public TransportGuideService(
        DespachosDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user, ISapDocumentLookupService sap,
        IDteEmissionService dte, TimeProvider time, INotificationService notifications, ILogger<TransportGuideService>? logger = null)
    {
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
        if (settings is not { GuidesEnabled: true })
            return new GuidePreview(false, Array.Empty<PlannedGuideView>(), Array.Empty<string>(), Array.Empty<string>());

        var (plan, errors) = await PlanAsync(m, settings, previewSnapshot: m.Status == ManifestStatuses.Draft);
        var views = plan?.Guides.Select(g => View(g, m)).ToList() ?? new List<PlannedGuideView>();
        return new GuidePreview(true, views, errors, plan?.Warnings ?? Array.Empty<string>());
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
        // Con guías ya planificadas se sigue aunque luego se desactive la emisión (si no, el manifiesto queda atascado).
        var settings = guides.Count > 0
            ? await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == CompanyId) ?? new DespachosSetting()
            : await RequireSettingsAsync();

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
            if (plan is null || planErrors.Count > 0) return new GuideEmissionOutcome(false, 0, 0, planErrors);
            guides = Persist(m, settings, plan);
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

        var issued = guides.Count(g => g.Status == TransportGuideStatuses.Issued);
        var pending = guides.Count - issued;
        if (pending > 0)
        {
            AddEvent(m, ManifestEventTypes.GuideFailed, new { issued, pending, errors });
            await _db.SaveChangesAsync();
            await NotifyFailureAsync(m, pending, errors);
        }
        // "Todas emitidas" exige al menos una guía y todas emitidas: nunca se despacha con cero guías.
        return new GuideEmissionOutcome(guides.Count > 0 && pending == 0, issued, pending, errors);
    }

    public async Task<byte[]> GetPdfAsync(Guid manifestId, int? sequence)
    {
        if (!await _db.Manifests.AnyAsync(x => x.Id == manifestId && x.CompanyId == CompanyId))
            throw new InvalidOperationException("El manifiesto no existe.");
        var guides = await _db.TransportGuides.AsNoTracking()
            .Where(g => g.CompanyId == CompanyId && g.ManifestId == manifestId && g.Status == TransportGuideStatuses.Issued
                        && (sequence == null || g.Sequence == sequence))
            .OrderBy(g => g.Sequence).ToListAsync();
        if (guides.Count == 0) throw new InvalidOperationException("No hay guías emitidas para imprimir.");

        var pdfs = new List<byte[]>();
        foreach (var g in guides) pdfs.Add(await _dte.GetPdfAsync(g.EmissionKey, cedible: false, copies: 1));
        return pdfs.Count == 1 ? pdfs[0] : Merge(pdfs);
    }

    public async Task AbandonPendingAsync(Guid manifestId, string reason)
    {
        var guides = await _db.TransportGuides
            .Where(g => g.CompanyId == CompanyId && g.ManifestId == manifestId).ToListAsync();
        if (guides.Any(g => g.Status == TransportGuideStatuses.Issued))
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
        await _db.SaveChangesAsync();
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
            if (d.DocumentKind != DocumentKinds.Invoice)
            {
                docs.Add((d, new SapDocumentLines(null, null, Array.Empty<SapDocumentLine>())));
                continue;
            }
            try
            {
                docs.Add((d, await _sap.GetLinesAsync(SapQueryKinds.InvoiceLines, d.SapDocEntry)));
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"Folio {d.Folio}: {ex.Message}");
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
            settings.GuideItemCodePlacement, settings.GuideMaxLines));
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
                TransferIndicator = settings.TransferIndicator, Status = TransportGuideStatuses.Planned, CreatedAt = now,
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
