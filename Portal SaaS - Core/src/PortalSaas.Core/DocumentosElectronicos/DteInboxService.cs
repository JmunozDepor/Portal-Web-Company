using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>
/// Bandeja de recibidos de la compañía de la sesión para módulos (IDteInboxService). Respuesta idempotente: una
/// respuesta enviada o externa nunca se vuelve a enviar; una pendiente solo se reintenta con su misma clave; una
/// fallida acepta una clave nueva. Lo que el operador dice "ya respondido" queda pendiente y lo confirma la próxima
/// sincronización (DteReceptionSyncService), que es la fuente de verdad.
/// </summary>
public sealed class DteInboxService : IDteInboxService
{
    private const string NotEnabled = "La recepción de documentos no está habilitada para esta compañía.";

    private readonly PortalSaasDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly DteSettingsService _settings;
    private readonly DteReceptionProviderRegistry _providers;
    private readonly DteReceptionSyncService _sync;
    private readonly TimeProvider _clock;

    public DteInboxService(PortalSaasDbContext db, ICurrentCompanyAccessor company, DteSettingsService settings,
        DteReceptionProviderRegistry providers, DteReceptionSyncService sync, TimeProvider clock)
    {
        _db = db;
        _company = company;
        _settings = settings;
        _providers = providers;
        _sync = sync;
        _clock = clock;
    }

    private Guid CompanyId => _company.HasCompany
        ? _company.CompanyId
        : throw new InvalidOperationException("Seleccione una compañía antes de operar la bandeja de documentos recibidos.");

    private IQueryable<DteReceived> Docs => _db.DteReceived.Where(x => x.CompanyId == CompanyId);

    public async Task<DteInboxStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var s = await _db.CompanyDteSettings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == CompanyId, ct);
        if (s is null)
            return new DteInboxStatus(false, null, Array.Empty<int>(), Array.Empty<string>(), false, false, 60, 30, null, null);
        var caps = _providers.Contains(s.ProviderCode) ? _providers.Get(s.ProviderCode).Capabilities : null;
        return new DteInboxStatus(
            s.ReceptionEnabled && caps is not null, s.ProviderCode,
            caps?.RespondableTypes ?? Array.Empty<int>(), caps?.SupportedActions ?? Array.Empty<string>(),
            caps?.SupportsResponseNote ?? false, caps?.SupportsDocumentDownload ?? false,
            s.ReceptionInitialDays, s.ReceptionIntervalMinutes, s.ReceptionLastSyncAt, s.ReceptionLastError);
    }

    public async Task<IReadOnlyList<DteReceivedDto>> ListAsync(DteReceivedFilter filter, CancellationToken ct = default)
    {
        var q = Docs.AsNoTracking();
        if (filter.ReceivedFrom is { } from)
        {
            var fromUtc = StartOfChileDay(from);
            q = q.Where(x => x.SiiReceivedAt >= fromUtc || (x.SiiReceivedAt == null && x.IssueDate >= from));
        }
        if (filter.ReceivedTo is { } to)
        {
            var toUtc = StartOfChileDay(to.AddDays(1));
            q = q.Where(x => x.SiiReceivedAt < toUtc || (x.SiiReceivedAt == null && x.IssueDate <= to));
        }
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            // ToLower en los dos lados: Contains distingue mayúsculas en Postgres (no en SQL Server).
            var text = filter.Text.Trim().ToLowerInvariant();
            var folio = long.TryParse(text, out var n) ? n : -1;
            q = q.Where(x => x.IssuerTaxId.ToLower().Contains(text) || x.IssuerName.ToLower().Contains(text) || x.Folio == folio);
        }
        var rows = await q.OrderByDescending(x => x.SiiReceivedAt).ThenByDescending(x => x.Id)
            .Take(Math.Clamp(filter.Limit, 1, 5000)).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<DteReceivedDto>> ListChangedSinceAsync(DateTimeOffset since, long afterId, int max, CancellationToken ct = default)
    {
        var rows = await Docs.AsNoTracking()
            .Where(x => x.UpdatedAt > since || (x.UpdatedAt == since && x.Id > afterId))
            .OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id)
            .Take(Math.Clamp(max, 1, 5000)).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<DteReceivedDto?> GetAsync(long id, CancellationToken ct = default)
    {
        var row = await Docs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : ToDto(row);
    }

    public async Task<IReadOnlyList<DteReceivedLine>> GetLinesAsync(long id, CancellationToken ct = default)
    {
        var xml = await Docs.Where(x => x.Id == id).Select(x => x.Xml).FirstOrDefaultAsync(ct);
        return DteReceivedXmlReader.ReadLines(xml);
    }

    public async Task<byte[]?> GetPdfAsync(long id, CancellationToken ct = default)
    {
        var row = await Docs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return null;
        var resolved = await _settings.ResolveReceptionAsync(CompanyId);
        if (resolved is not { } r || (!r.Provider.Capabilities.SupportsDocumentDownload && !IsLink(row.ProviderUrl))) return null;
        try
        {
            return await r.Provider.GetPdfAsync(r.Ctx, row.IssuerTaxId, row.DteType, row.Folio, row.ProviderUrl, ct);
        }
        catch (DteProviderUnavailableException)
        {
            // El módulo no conoce las excepciones de la plataforma: sin PDF, la pantalla lo informa.
            return null;
        }
    }

    private static bool IsLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    public async Task<DteRespondResult> RespondAsync(DteRespondRequest request, CancellationToken ct = default)
    {
        var row = await Docs.FirstOrDefaultAsync(x => x.Id == request.ReceivedId, ct)
            ?? throw new InvalidOperationException("No se encontró el documento recibido en la compañía activa.");

        var caps = _providers.Contains(row.ProviderCode) ? _providers.Get(row.ProviderCode).Capabilities : null;
        if (caps is null || !caps.RespondableTypes.Contains(row.DteType) || !caps.SupportedActions.Contains(request.Action))
            return new DteRespondResult(DteRespondOutcome.NotRespondable, $"El documento tipo {row.DteType} no admite esa respuesta.");
        if (row.NotRespondableReason is { } notRespondable)
            return new DteRespondResult(DteRespondOutcome.NotRespondable, notRespondable);

        switch (row.ResponseStatus)
        {
            case DteResponseStatuses.Sent or DteResponseStatuses.External:
                return new DteRespondResult(DteRespondOutcome.AlreadyResponded, null);
            case DteResponseStatuses.Pending when row.ResponseKey != request.ResponseKey:
                return new DteRespondResult(DteRespondOutcome.AlreadyResponded, null);
        }

        var now = _clock.GetUtcNow();
        row.ResponseStatus = DteResponseStatuses.Pending;
        row.ResponseKey = request.ResponseKey;
        row.ResponseAction = request.Action;
        row.ResponseReason = Truncate(request.Reason, 500);
        row.ResponseActor = Truncate(request.Actor, 80);
        row.ResponseAttempts++;
        row.LastError = null;
        row.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        var resolved = await _settings.ResolveReceptionAsync(CompanyId);
        if (resolved is not { } r)
            return await FailAsync(row, NotEnabled, ct);

        DteRespondProviderResult result;
        try
        {
            result = await r.Provider.RespondAsync(r.Ctx, row.IssuerTaxId, row.DteType, row.Folio, request.Action, ct);
        }
        catch (DteProviderUnavailableException ex)
        {
            return await FailAsync(row, ex.Message, ct);
        }

        if (result.Ok)
        {
            row.ResponseStatus = DteResponseStatuses.Sent;
            row.RespondedAt = _clock.GetUtcNow();
            row.UpdatedAt = row.RespondedAt.Value;
            await _db.SaveChangesAsync(ct);
            if (!string.IsNullOrWhiteSpace(request.Reason) && r.Provider.Capabilities.SupportsResponseNote)
            {
                try
                {
                    await r.Provider.SetNoteAsync(r.Ctx, row.IssuerTaxId, row.DteType, row.Folio, request.Reason, ct);
                }
                catch (Exception ex) when (ex is DteProviderUnavailableException or NotSupportedException)
                {
                    // Best-effort: la respuesta ya quedó registrada ante el SII.
                }
            }
            return new DteRespondResult(DteRespondOutcome.Sent, null);
        }

        if (result.AlreadyResponded)
        {
            row.LastError = "Ya respondida en el operador; se confirma en la próxima sincronización.";
            row.UpdatedAt = _clock.GetUtcNow();
            await _db.SaveChangesAsync(ct);
            return new DteRespondResult(DteRespondOutcome.PendingConfirmation, result.Error);
        }

        return await FailAsync(row, result.Error ?? "El operador rechazó la respuesta sin detalle.", ct);
    }

    private async Task<DteRespondResult> FailAsync(DteReceived row, string error, CancellationToken ct)
    {
        row.ResponseStatus = DteResponseStatuses.Failed;
        row.LastError = Truncate(error, 1000);
        row.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return new DteRespondResult(DteRespondOutcome.Failed, error);
    }

    public async Task SetPurchaseOrderRefsAsync(long id, IReadOnlyList<int> manualDocNums, Guid userId, CancellationToken ct = default)
    {
        if (manualDocNums.Any(n => n <= 0)) throw new InvalidOperationException("El número de OC debe ser mayor que cero.");
        var row = await Docs.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("No se encontró el documento recibido en la compañía activa.");
        var now = _clock.GetUtcNow();
        var current = DteReceivedJson.PurchaseOrderRefs(row.PurchaseOrderRefsJson);
        var fromDte = current.Where(r => r.Source == DtePurchaseOrderRefSources.Dte).ToList();
        var manual = current.Where(r => r.Source == DtePurchaseOrderRefSources.Manual).ToDictionary(r => r.DocNum);
        var updated = fromDte
            .Concat(manualDocNums.Distinct()
                .Where(n => fromDte.All(d => d.DocNum != n))
                .Select(n => manual.TryGetValue(n, out var kept) ? kept : new DtePurchaseOrderRef(n, DtePurchaseOrderRefSources.Manual, userId, now)))
            .ToList();
        row.PurchaseOrderRefsJson = DteReceivedJson.PurchaseOrderRefs(updated);
        row.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkProcessingAsync(long id, string moduleCode, string state, string? reference, CancellationToken ct = default)
    {
        var row = await Docs.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("No se encontró el documento recibido en la compañía activa.");
        var now = _clock.GetUtcNow();
        row.ProcessingModule = Truncate(moduleCode, 50);
        row.ProcessingState = Truncate(state, 30);
        row.ProcessingRef = Truncate(reference, 80);
        row.ProcessingAt = now;
        row.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RequestSyncAsync(CancellationToken ct = default)
    {
        var result = await _sync.SyncCompanyAsync(CompanyId, ct);
        if (result.Error is { } error) throw new InvalidOperationException(error);
    }

    private static DateTimeOffset StartOfChileDay(DateOnly day) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), ChileTime.Zone), TimeSpan.Zero);

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];

    private static DteReceivedDto ToDto(DteReceived x) => new()
    {
        Id = x.Id, ProviderCode = x.ProviderCode, IssuerTaxId = x.IssuerTaxId, IssuerName = x.IssuerName, DteType = x.DteType,
        Folio = x.Folio, IssueDate = x.IssueDate, SiiReceivedAt = x.SiiReceivedAt, NetAmount = x.NetAmount,
        ExemptAmount = x.ExemptAmount, VatAmount = x.VatAmount, TotalAmount = x.TotalAmount, SiiStatus = x.SiiStatus,
        CommercialStatus = x.CommercialStatus, ClaimStatus = x.ClaimStatus, IsAssigned = x.IsAssigned, IsVoided = x.IsVoided,
        NotRespondableReason = x.NotRespondableReason, HasXml = !string.IsNullOrEmpty(x.Xml), HasPdf = IsLink(x.ProviderUrl),
        PaymentForm = x.PaymentForm, DueDate = x.DueDate,
        References = DteReceivedJson.References(x.ReferencesJson),
        PurchaseOrderRefs = DteReceivedJson.PurchaseOrderRefs(x.PurchaseOrderRefsJson),
        ResponseStatus = x.ResponseStatus, ResponseAction = x.ResponseAction, ResponseReason = x.ResponseReason,
        ResponseActor = x.ResponseActor, RespondedAt = x.RespondedAt, LastError = x.LastError,
        ProcessingModule = x.ProcessingModule, ProcessingState = x.ProcessingState, ProcessingRef = x.ProcessingRef,
        UpdatedAt = x.UpdatedAt,
    };
}
