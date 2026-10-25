using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modulo.Despachos.Data;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Dominio.Guias;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.Despachos.Servicios;

/// <summary>Guías manuales (ver IManualGuideService). El borrador se guarda como DteDocument en transport_guides.document_json.</summary>
public sealed class ManualGuideService : IManualGuideService
{
    private const int MaxNumberAttempts = 3;
    private static readonly JsonSerializerOptions Json = new();

    /// <summary>Motivos sembrados la primera vez: (código SII, nombre, afecto a IVA, activo).</summary>
    private static readonly (int Code, string Name, bool Vat, bool Active)[] SeedMotives =
    {
        (2, "Ventas por efectuar", true, false),
        (4, "Entrega gratuita", false, false),
        (5, "Traslados internos", false, true),
        (6, "Otros traslados no venta", true, true),
        (7, "Guía de devolución", true, false),
    };

    private readonly DespachosDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly ISapDocumentLookupService _sap;
    private readonly IDteEmissionService _dte;
    private readonly TimeProvider _time;
    private readonly INotificationService _notifications;
    private readonly ILogger<ManualGuideService>? _logger;
    private readonly IMasterDataService? _master;

    public ManualGuideService(
        DespachosDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user, ISapDocumentLookupService sap,
        IDteEmissionService dte, TimeProvider time, INotificationService notifications, ILogger<ManualGuideService>? logger = null,
        IMasterDataService? master = null)
    {
        _master = master;
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
    private DateOnly TodayChile => DateOnly.FromDateTime(ChileClock.ToLocal(UtcNow));

    // ---------------------------------------------------------------- Motivos

    public async Task<IReadOnlyList<TransferMotive>> ListMotivesAsync(bool includeInactive = false)
    {
        await EnsureMotivesAsync();
        return await _db.TransferMotives.AsNoTracking()
            .Where(m => m.CompanyId == CompanyId && (includeInactive || m.IsActive))
            .OrderBy(m => m.Code).ToListAsync();
    }

    private async Task EnsureMotivesAsync()
    {
        if (await _db.TransferMotives.AnyAsync(m => m.CompanyId == CompanyId)) return;
        foreach (var (code, name, vat, active) in SeedMotives)
            _db.TransferMotives.Add(new TransferMotive
            {
                CompanyId = CompanyId, Code = code, Name = name, VatAffected = vat, IsActive = active,
                CreatedAt = UtcNow, CreatedBy = _user.UserId,
            });
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Otra solicitud los sembró primero (índice único): se usan los existentes.
            foreach (var e in _db.ChangeTracker.Entries<TransferMotive>().Where(e => e.State == EntityState.Added).ToList())
                e.State = EntityState.Detached;
        }
    }

    public async Task SaveMotiveAsync(TransferMotive motive)
    {
        if (motive.Code is < 1 or > 9) throw new InvalidOperationException("El código del motivo debe ser un IndTraslado del SII (1 a 9).");
        if (string.IsNullOrWhiteSpace(motive.Name)) throw new InvalidOperationException("Ingrese el nombre del motivo.");
        await EnsureMotivesAsync();
        if (await _db.TransferMotives.AnyAsync(m => m.CompanyId == CompanyId && m.Code == motive.Code && m.Id != motive.Id))
            throw new InvalidOperationException($"Ya existe un motivo con el código {motive.Code}.");
        var row = motive.Id == 0 ? null
            : await _db.TransferMotives.FirstOrDefaultAsync(m => m.CompanyId == CompanyId && m.Id == motive.Id)
              ?? throw new InvalidOperationException("El motivo no existe.");
        if (row is null)
        {
            row = new TransferMotive { CompanyId = CompanyId, CreatedAt = UtcNow, CreatedBy = _user.UserId };
            _db.TransferMotives.Add(row);
        }
        else
        {
            row.UpdatedAt = UtcNow;
            row.UpdatedBy = _user.UserId;
        }
        row.Code = motive.Code;
        row.Name = motive.Name.Trim();
        row.VatAffected = motive.VatAffected;
        row.IsActive = motive.IsActive;
        await _db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- Borrador

    public async Task<Guid> CreateAsync(ManualGuideInput input)
    {
        for (var attempt = 1; ; attempt++)
        {
            var g = new TransportGuide
            {
                Id = Guid.NewGuid(), CompanyId = CompanyId, Source = TransportGuideSources.Manual, EmissionKey = Guid.NewGuid(),
                Number = await NextNumberAsync(), Status = TransportGuideStatuses.Draft, CreatedAt = UtcNow, CreatedBy = _user.UserId,
            };
            await ApplyAsync(g, input);
            _db.TransportGuides.Add(g);
            try
            {
                await _db.SaveChangesAsync();
                return g.Id;
            }
            catch (DbUpdateException) when (attempt < MaxNumberAttempts)
            {
                // Otro usuario tomó el mismo número entre la lectura y la escritura: se descarta y se reintenta.
                _db.Entry(g).State = EntityState.Detached;
            }
        }
    }

    private async Task<int> NextNumberAsync() =>
        (await _db.TransportGuides.Where(g => g.CompanyId == CompanyId && g.Number != null).MaxAsync(g => g.Number) ?? 0) + 1;

    public async Task UpdateAsync(Guid id, ManualGuideInput input)
    {
        var g = await LoadAsync(id);
        if (g.Status is not (TransportGuideStatuses.Draft or TransportGuideStatuses.InManifest or TransportGuideStatuses.Failed))
            throw new InvalidOperationException($"La guía {Code(g)} está {TransportGuideStatuses.Label(g.Status).ToLowerInvariant()}: no se puede editar.");
        // En un manifiesto el transporte es el del manifiesto (se toma al despacharlo).
        var inManifest = await ManifestNumberOfAsync(g.Id) is not null;
        if (inManifest) input = input with { TransportMode = ManualTransportModes.Manifest };
        await ApplyAsync(g, input);
        if (inManifest) await SyncManifestDocumentsAsync(g);
        if (g.Status == TransportGuideStatuses.Failed)
        {
            // Corregida: se reemite con la misma llave (la pasarela acepta otro contenido si falló sin folio), sola o
            // al reintentar las guías del manifiesto.
            g.Status = inManifest ? TransportGuideStatuses.InManifest : TransportGuideStatuses.Draft;
            g.LastError = null;
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>Valida (formulario + motivo + origen) y deja el documento y las columnas resumen en la fila.</summary>
    private async Task ApplyAsync(TransportGuide g, ManualGuideInput input)
    {
        await EnsureMotivesAsync();
        var settings = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == CompanyId);
        var errors = ManualGuideBuilder.Validate(input, Math.Clamp(settings?.GuideMaxLines ?? GuidePlanner.MaxLines, 1, GuidePlanner.MaxLines)).ToList();
        var motive = await _db.TransferMotives.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyId == CompanyId && m.Code == input.TransferIndicator && m.IsActive);
        if (motive is null && input.TransferIndicator > 0) errors.Insert(0, "El motivo de traslado no existe o está inactivo.");
        var origin = await OriginAsync(input.OriginId);
        if (origin is null) errors.Insert(0, "Elija un origen con dirección y comuna (Despachos > Orígenes).");
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));

        var doc = ManualGuideBuilder.BuildDraft(input, origin!, motive!.VatAffected);
        var (net, vat, total) = ManualGuideBuilder.Totals(doc.Lines);
        g.IssueDate = input.IssueDate;
        g.TransferIndicator = input.TransferIndicator;
        g.DispatchType = input.DispatchType;
        g.OriginId = input.OriginId;
        g.ReceiverTaxId = doc.Receiver.TaxId;
        g.ReceiverName = doc.Receiver.Name;
        g.ReceiverCardCode = string.IsNullOrWhiteSpace(input.ReceiverCardCode) ? null : input.ReceiverCardCode.Trim();
        g.DestinationAddress = doc.Transport!.DestinationAddress ?? "";
        g.DestinationCommune = doc.Transport.DestinationCommune ?? "";
        g.DestinationCity = doc.Transport.DestinationCity;
        g.LineCount = doc.Lines.Count;
        g.TotalQuantity = doc.Lines.Sum(l => l.Quantity);
        g.NetAmount = net;
        g.VatAmount = vat;
        g.TotalAmount = total;
        g.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        g.TransportMode = input.TransportMode;
        g.DocumentJson = JsonSerializer.Serialize(doc, Json);
    }

    /// <summary>
    /// La fila del manifiesto en borrador que lleva la guía refleja la guía editada (receptor, destino, comuna, total y
    /// unidades alimentan la hoja de ruta y la tarifa).
    /// </summary>
    private async Task SyncManifestDocumentsAsync(TransportGuide g)
    {
        var docs = await (from d in _db.ManifestDocuments
                          join m in _db.Manifests on d.ManifestId equals m.Id
                          where d.CompanyId == CompanyId && d.TransportGuideId == g.Id && m.Status == ManifestStatuses.Draft
                          select d).ToListAsync();
        if (docs.Count == 0) return;
        var commune = _master is null || string.IsNullOrWhiteSpace(g.DestinationCommune)
            ? null : await _master.FindCommuneByNameAsync(g.DestinationCommune);
        foreach (var d in docs)
        {
            d.CustomerCode = g.ReceiverCardCode ?? "";
            d.CustomerName = g.ReceiverName;
            d.CustomerTaxId = g.ReceiverTaxId;
            d.DestinationAddress = g.DestinationAddress;
            d.DestinationCommuneName = g.DestinationCommune;
            d.DestinationCommuneId = commune?.Id;
            d.DocumentDate = g.IssueDate ?? d.DocumentDate;
            d.TotalAmount = g.TotalAmount ?? 0;
            d.Units = g.TotalQuantity;
        }
    }

    private async Task<ManualGuideOrigin?> OriginAsync(long originId)
    {
        var o = await _db.Origins.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == originId && x.IsActive);
        if (o is null || string.IsNullOrWhiteSpace(o.Address) || o.CommuneId is null) return null;
        var commune = await _db.Communes.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == CompanyId && c.Id == o.CommuneId);
        return commune is null ? null : new ManualGuideOrigin(o.Address.Trim(), commune.Name, null);
    }

    public async Task<TransportGuide?> GetAsync(Guid id) =>
        await _db.TransportGuides.AsNoTracking()
            .FirstOrDefaultAsync(g => g.CompanyId == CompanyId && g.Id == id && g.Source == TransportGuideSources.Manual);

    public async Task<TransportGuide?> GetByNumberAsync(int number) =>
        await _db.TransportGuides.AsNoTracking()
            .FirstOrDefaultAsync(g => g.CompanyId == CompanyId && g.Number == number && g.Source == TransportGuideSources.Manual);

    public async Task<ManualGuideInput> GetInputAsync(Guid id)
    {
        var g = await GetAsync(id) ?? throw new InvalidOperationException("La guía manual no existe.");
        return ManualGuideBuilder.ToInput(Document(g), g);
    }

    public async Task AnnulAsync(Guid id, string reason)
    {
        var g = await LoadAsync(id);
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Indique el motivo de la anulación.");
        if (g.Status != TransportGuideStatuses.Draft)
            throw new InvalidOperationException(g.Status == TransportGuideStatuses.InManifest
                ? $"La guía {Code(g)} está en un manifiesto: quítela del manifiesto antes de anularla."
                : $"Solo se anula un borrador; la guía {Code(g)} está {TransportGuideStatuses.Label(g.Status).ToLowerInvariant()}.");
        // Una emisión que alcanzó a quedar en la pasarela (proveedor caído) se abandona para liberar su folio.
        if (await _dte.GetAsync(g.EmissionKey) is not null) await _dte.AbandonAsync(g.EmissionKey, reason.Trim());
        g.Status = TransportGuideStatuses.Annulled;
        g.AnnulReason = reason.Trim();
        await _db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- Emisión sola

    public async Task<byte[]> PreviewPdfAsync(Guid id)
    {
        var g = await GetAsync(id) ?? throw new InvalidOperationException("La guía manual no existe.");
        var doc = ManualGuideBuilder.ForEmission(Document(g), g.Notes, null, Max(g.IssueDate ?? TodayChile, TodayChile));
        var preview = await _dte.PreviewAsync(doc);
        return preview.Pdf ?? throw new InvalidOperationException(
            "No hay vista previa: " + string.Join("; ", preview.Errors.Select(e => e.Message)));
    }

    public async Task<TransportGuide> EmitAsync(Guid id)
    {
        var g = await LoadAsync(id);
        switch (g.Status)
        {
            case TransportGuideStatuses.Issued:
                return g;
            case TransportGuideStatuses.Annulled:
                throw new InvalidOperationException($"La guía {Code(g)} está anulada.");
        }
        if (await ManifestNumberOfAsync(g.Id) is { } number)
            throw new InvalidOperationException($"Esta guía viaja en el manifiesto N° {number}: se emite al despacharlo.");

        // Estado real en la pasarela: una emisión en curso o emitida no se reenvía con otro contenido.
        var current = await _dte.GetAsync(g.EmissionKey);
        if (current is not null && !(current.Status == DteEmissionStatuses.Failed && current.Folio is null)
                                && current.Status != DteEmissionStatuses.Pending)
        {
            await ApplyAndSaveAsync(g, current);
            return g;
        }

        var input = ManualGuideBuilder.ToInput(Document(g), g);
        var settings = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == CompanyId);
        var errors = ManualGuideBuilder.Validate(input, Math.Clamp(settings?.GuideMaxLines ?? GuidePlanner.MaxLines, 1, GuidePlanner.MaxLines));
        if (input.TransportMode == ManualTransportModes.Manifest)
            errors = errors.Append("Elija el transporte de la guía (de los maestros o particular) antes de emitirla sola.").ToList();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));

        // Primer envío: la fecha efectiva (no al pasado) queda guardada. Una emisión en curso (proveedor caído) se
        // reenvía con su fecha ya guardada: la pasarela rechaza la misma llave con otro contenido.
        if (current is null || current.Status != DteEmissionStatuses.Pending)
            g.IssueDate = Max(g.IssueDate ?? TodayChile, TodayChile);
        var doc = ManualGuideBuilder.ForEmission(Document(g), g.Notes, null, g.IssueDate ?? TodayChile);
        DteEmissionResult r;
        try
        {
            r = await _dte.EmitAsync(new DteEmissionRequest(g.EmissionKey, TransportGuideService.SourceModule, $"manual:{Code(g)}", doc));
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            // Si la pasarela tiene una emisión vigente para la llave, manda su estado (no se marca rechazada una guía
            // que el proveedor todavía puede emitir).
            r = await _dte.GetAsync(g.EmissionKey) is { } live && !(live.Status == DteEmissionStatuses.Failed && live.Folio is null)
                ? live
                : new DteEmissionResult(g.EmissionKey, DteEmissionStatuses.Failed, null, ex.Message, "", "");
        }
        await ApplyAndSaveAsync(g, r);
        if (g.Status == TransportGuideStatuses.Failed) await NotifyFailureAsync(g);
        return g;
    }

    private async Task ApplyAndSaveAsync(TransportGuide g, DteEmissionResult r)
    {
        Apply(g, r, UtcNow, _user.UserId);
        await _db.SaveChangesAsync();
    }

    /// <summary>Estado de la emisión en la pasarela aplicado a la guía (compartido con la emisión al despachar).</summary>
    internal static void Apply(TransportGuide g, DteEmissionResult r, DateTime utcNow, Guid? userId)
    {
        g.Folio = r.Folio;
        g.LastError = r.Error is { Length: > 1000 } e ? e[..1000] : r.Error;
        switch (r.Status)
        {
            case DteEmissionStatuses.Issued:
                g.Status = TransportGuideStatuses.Issued;
                g.IssuedAt ??= utcNow;
                g.IssuedBy ??= userId;
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

    public async Task<byte[]> GetPdfAsync(Guid id)
    {
        var g = await GetAsync(id) ?? throw new InvalidOperationException("La guía manual no existe.");
        if (g.Status != TransportGuideStatuses.Issued) throw new InvalidOperationException($"La guía {Code(g)} todavía no está emitida.");
        return await _dte.GetPdfAsync(g.EmissionKey, cedible: false, copies: 1);
    }

    public Task<SapBusinessPartner?> FindReceiverAsync(string taxId) => _sap.FindBusinessPartnerAsync(taxId);

    // ---------------------------------------------------------------- Lista

    public async Task<IReadOnlyList<GuideListItem>> ListAsync(GuideFilter filter)
    {
        var q = _db.TransportGuides.AsNoTracking().Where(g => g.CompanyId == CompanyId);
        if (!string.IsNullOrWhiteSpace(filter.Source)) q = q.Where(g => g.Source == filter.Source);
        if (!string.IsNullOrWhiteSpace(filter.Status)) q = q.Where(g => g.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Receiver))
        {
            var text = filter.Receiver.Trim().ToLower();
            q = q.Where(g => g.ReceiverName.ToLower().Contains(text) || g.ReceiverTaxId.Contains(text));
        }
        // Fechas en la consulta (antes del tope de filas): la del borrador manual, o la de creación para las del
        // manifiesto (se afina abajo con la fecha de emisión).
        if (filter.From is { } from)
        {
            var fromUtc = ChileClock.ToUtc(from.AddDays(-1).ToDateTime(TimeOnly.MinValue));
            q = q.Where(g => g.IssueDate != null ? g.IssueDate >= from : g.CreatedAt >= fromUtc);
        }
        if (filter.To is { } to)
        {
            var toUtc = ChileClock.ToUtc(to.AddDays(2).ToDateTime(TimeOnly.MinValue));
            q = q.Where(g => g.IssueDate != null ? g.IssueDate <= to : g.CreatedAt < toUtc);
        }
        var rows = await q.OrderByDescending(g => g.CreatedAt).Take(1000).ToListAsync();
        var manifestIds = rows.Where(g => g.ManifestId != null).Select(g => g.ManifestId!.Value).Distinct().ToList();
        var manifests = await _db.Manifests.AsNoTracking().Where(m => m.CompanyId == CompanyId && manifestIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Number);

        return rows
            .Select(g =>
            {
                int? manifestNumber = g.ManifestId is { } mid && manifests.TryGetValue(mid, out var n) ? n : null;
                var date = g.IssueDate ?? DateOnly.FromDateTime(ChileClock.ToLocal(g.IssuedAt ?? g.CreatedAt));
                var code = g.Source == TransportGuideSources.Manual ? Code(g) : $"Manifiesto N° {manifestNumber} / guía {g.Sequence}";
                return new GuideListItem(g.Id, g.Source, code, g.ManifestId, manifestNumber, g.Folio, date, g.ReceiverName,
                    g.ReceiverTaxId, g.DestinationCommune, g.TransferIndicator, g.TotalAmount, g.Status, g.Sequence);
            })
            .Where(x => (filter.From is null || x.Date >= filter.From) && (filter.To is null || x.Date <= filter.To))
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Code, StringComparer.Ordinal)
            .ToList();
    }

    // ---------------------------------------------------------------- Helpers

    private async Task<TransportGuide> LoadAsync(Guid id) =>
        await _db.TransportGuides.FirstOrDefaultAsync(g => g.CompanyId == CompanyId && g.Id == id && g.Source == TransportGuideSources.Manual)
        ?? throw new InvalidOperationException("La guía manual no existe.");

    private async Task<int?> ManifestNumberOfAsync(Guid guideId) =>
        await (from d in _db.ManifestDocuments
               join m in _db.Manifests on d.ManifestId equals m.Id
               where d.CompanyId == CompanyId && d.TransportGuideId == guideId && m.Status != ManifestStatuses.Annulled
               select (int?)m.Number).FirstOrDefaultAsync();

    private static DteDocument Document(TransportGuide g) => JsonSerializer.Deserialize<DteDocument>(g.DocumentJson, Json)
        ?? throw new InvalidOperationException("La guía no tiene contenido.");

    internal static string Code(TransportGuide g) => ManualGuideCode.Format(g.Number ?? 0);

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private async Task NotifyFailureAsync(TransportGuide g)
    {
        try
        {
            await _notifications.TryEnqueueAsync(_user.UserId, $"Guía manual {Code(g)} rechazada",
                $"La guía {Code(g)} ({g.ReceiverName}) no se emitió: {g.LastError ?? "sin respuesta del proveedor"}. Corríjala y vuelva a emitir.",
                NotificationSeverity.Warning, $"/despachos/guias-manuales/detalle?id={g.Id:D}");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No se pudo avisar el rechazo de la guía manual {Code}.", Code(g));
        }
    }
}
