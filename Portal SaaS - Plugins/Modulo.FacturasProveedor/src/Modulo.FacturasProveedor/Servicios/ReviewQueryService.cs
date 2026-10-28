using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Data;
using Modulo.FacturasProveedor.Dominio;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Models;
using Modulo.FacturasProveedor.Pages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.FacturasProveedor.Servicios;

/// <summary>StatusGroup: pending (aviso, por decidir, por enviar), all, accepted, rejected o simulation.</summary>
public sealed record ReviewListFilter(DateOnly? From, DateOnly? To, string StatusGroup, string? Text);

/// <summary>
/// Una fila de la bandeja. Matches: en simulación, si la decisión simulada coincide con lo respondido en el operador.
/// SiiReceivedAt (hora de registro), PaymentForm, DueDate, HasPdf y ReferenceLabel (documentos tributarios referenciados,
/// ej. la factura que anula una nota de crédito) vienen de la bandeja de plataforma.
/// </summary>
public sealed record ReviewRow(
    long Id, long ReceivedId, DateOnly SiiReceivedOn, string IssuerName, string IssuerTaxId, int DteType, long Folio, long TotalAmount,
    IReadOnlyList<int> PurchaseOrders, string Status, string? Summary, DateOnly LegalDeadlineOn, DateOnly? WarnDeadlineOn,
    string? DecidedAction, bool IsSimulated, string? OperatorAction, bool? Matches, Guid ConcurrencyStamp,
    DateTimeOffset? SiiReceivedAt = null, string? PaymentForm = null, DateOnly? DueDate = null, bool HasPdf = false,
    string? ReferenceLabel = null);

/// <summary>Una página de la bandeja y el total de filas del filtro.</summary>
public sealed record ReviewPage(IReadOnlyList<ReviewRow> Rows, int Total);

public sealed record ReviewKpis(int InReview, int Warning, int DueIn2Days, int AcceptedMonth, int RejectedMonth);

/// <summary>Detalle de una revisión. Dte = el documento leído del XML (null si el operador no lo tiene).</summary>
public sealed record ReviewDetail(
    ReviewRow Row, DteReceivedDto Document, IReadOnlyList<FpReviewRuleResult> Rules, IReadOnlyList<DteReceivedLine> Lines,
    IReadOnlyList<FpReviewEvent> Events, string? AcceptWarning, bool CanDecide, bool CanAssociate, string? AssignedGroupName,
    string? RecommendedAction, string? Reason, string? DecisionSource, IReadOnlyList<string> ClaimActions, bool SupportsPdf,
    DteReceivedDocument? Dte = null);

public interface IReviewQueryService
{
    /// <summary>Todo lo filtrado (hasta 10.000 filas): exportar a Excel.</summary>
    Task<IReadOnlyList<ReviewRow>> ListAsync(ReviewListFilter filter, CancellationToken ct);
    /// <summary>Una página de lo filtrado, más recientes primero (page desde 1).</summary>
    Task<ReviewPage> ListPageAsync(ReviewListFilter filter, int page, int pageSize, CancellationToken ct);
    Task<ReviewKpis> GetKpisAsync(CancellationToken ct);
    Task<ReviewDetail?> GetDetailAsync(long id, CancellationToken ct);
    /// <summary>Facturas por decidir de los grupos del usuario (asignado, aprobador o suplente del proveedor).</summary>
    Task<IReadOnlyList<ReviewRow>> ListMineAsync(CancellationToken ct);
}

/// <summary>Lecturas de las pantallas (bandeja, detalle, mis aprobaciones). Une cada revisión con su documento de la bandeja de plataforma.</summary>
public sealed class ReviewQueryService : IReviewQueryService
{
    private const string BandejaMenu = "FacturasProveedor.bandeja";
    private const int MaxRows = 10_000;

    /// <summary>Tipos de documento tributario que pueden aparecer como referencia (las OC, 801, van en su columna).</summary>
    private static readonly HashSet<int> ReferenceableTypes = new() { 30, 32, 33, 34, 35, 38, 39, 41, 43, 46, 52, 55, 56, 60, 61 };

    private readonly FacturasProveedorDbContext _db;
    private readonly IDteInboxService _inbox;
    private readonly IApproverGroupService _groups;
    private readonly ISupplierProfileService _profiles;
    private readonly IReviewDecisionService _decisions;
    private readonly IModuleSettingsService _settings;
    private readonly ICurrentUserContext _user;
    private readonly ICurrentCompanyAccessor _company;
    private readonly TimeProvider _clock;

    public ReviewQueryService(FacturasProveedorDbContext db, IDteInboxService inbox, IApproverGroupService groups,
        ISupplierProfileService profiles, IReviewDecisionService decisions, IModuleSettingsService settings, ICurrentUserContext user,
        ICurrentCompanyAccessor company, TimeProvider clock)
    {
        _db = db;
        _inbox = inbox;
        _groups = groups;
        _profiles = profiles;
        _decisions = decisions;
        _settings = settings;
        _user = user;
        _company = company;
        _clock = clock;
    }

    private Guid CompanyId => _company.CompanyId;

    private DateOnly Today => DateOnly.FromDateTime(ChileClock.ToLocal(_clock.GetUtcNow().UtcDateTime));

    public async Task<IReadOnlyList<ReviewRow>> ListAsync(ReviewListFilter filter, CancellationToken ct)
    {
        var q = await FilteredAsync(filter, ct);
        return await ToRowsAsync(await Ordered(q).Take(MaxRows).ToListAsync(ct), ct);
    }

    public async Task<ReviewPage> ListPageAsync(ReviewListFilter filter, int page, int pageSize, CancellationToken ct)
    {
        var q = await FilteredAsync(filter, ct);
        var total = await q.CountAsync(ct);
        var reviews = await Ordered(q).Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new ReviewPage(await ToRowsAsync(reviews, ct), total);
    }

    private static IOrderedQueryable<FpInvoiceReview> Ordered(IQueryable<FpInvoiceReview> q) =>
        q.OrderByDescending(r => r.SiiReceivedOn).ThenByDescending(r => r.Id);

    /// <summary>
    /// Filtros de la bandeja, resueltos en la base. El texto busca proveedor (sin distinguir mayúsculas), RUT, folio u OC;
    /// las OC viven en la bandeja de plataforma, así que primero se buscan ahí los documentos que la referencian.
    /// </summary>
    private async Task<IQueryable<FpInvoiceReview>> FilteredAsync(ReviewListFilter filter, CancellationToken ct)
    {
        var q = _db.InvoiceReviews.AsNoTracking().Where(r => r.CompanyId == CompanyId);
        if (filter.From is { } from) q = q.Where(r => r.SiiReceivedOn >= from);
        if (filter.To is { } to) q = q.Where(r => r.SiiReceivedOn <= to);
        q = filter.StatusGroup switch
        {
            "pending" => q.Where(r => r.Status == ReviewStatuses.New || r.Status == ReviewStatuses.Warning || r.Status == ReviewStatuses.InReview
                || r.Status == ReviewStatuses.ToSend),
            "accepted" => q.Where(r => r.Status == ReviewStatuses.Accepted),
            "rejected" => q.Where(r => r.Status == ReviewStatuses.Rejected),
            "simulation" => q.Where(r => r.IsSimulated),
            _ => q,
        };
        if (string.IsNullOrWhiteSpace(filter.Text)) return q;

        var text = filter.Text.Trim();
        var lower = text.ToLower();
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return q.Where(r => r.IssuerName.ToLower().Contains(lower) || r.IssuerTaxId.Contains(text));
        var withPurchaseOrder = (await _inbox.ListAsync(new DteReceivedFilter(filter.From?.AddDays(-1), filter.To?.AddDays(1), null, 5000), ct))
            .Where(d => d.PurchaseOrderRefs.Any(po => po.DocNum == number)).Select(d => d.Id).ToList();
        return q.Where(r => r.IssuerName.ToLower().Contains(lower) || r.IssuerTaxId.Contains(text) || r.Folio == number
            || withPurchaseOrder.Contains(r.ReceivedId));
    }

    public async Task<ReviewKpis> GetKpisAsync(CancellationToken ct)
    {
        var today = Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var limit = today.AddDays(2);
        var rows = await _db.InvoiceReviews.AsNoTracking().Where(r => r.CompanyId == CompanyId)
            .Select(r => new { r.Status, r.LegalDeadlineOn, r.SiiReceivedOn }).ToListAsync(ct);
        return new ReviewKpis(
            rows.Count(r => r.Status == ReviewStatuses.InReview),
            rows.Count(r => r.Status == ReviewStatuses.Warning),
            rows.Count(r => ReviewStatuses.Pending.Contains(r.Status) && r.LegalDeadlineOn <= limit),
            rows.Count(r => r.Status == ReviewStatuses.Accepted && r.SiiReceivedOn >= monthStart),
            rows.Count(r => r.Status == ReviewStatuses.Rejected && r.SiiReceivedOn >= monthStart));
    }

    public async Task<ReviewDetail?> GetDetailAsync(long id, CancellationToken ct)
    {
        var review = await _db.InvoiceReviews.AsNoTracking().FirstOrDefaultAsync(r => r.CompanyId == CompanyId && r.Id == id, ct);
        if (review is null) return null;
        var doc = await _inbox.GetAsync(review.ReceivedId, ct);
        if (doc is null) return null;

        var row = (await ToRowsAsync(new[] { review }, ct)).Single();
        var rules = await _db.ReviewRuleResults.AsNoTracking().Where(r => r.ReviewId == id).OrderBy(r => r.Id).ToListAsync(ct);
        var events = await _db.ReviewEvents.AsNoTracking().Where(e => e.ReviewId == id)
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).ToListAsync(ct);
        // El XML se pide al operador la primera vez y queda archivado en la plataforma.
        var dte = await _inbox.GetDocumentAsync(review.ReceivedId, ct);
        var pending = review.Status is ReviewStatuses.InReview or ReviewStatuses.Warning;
        var canDecide = pending && await _decisions.CanDecideAsync(id, ct);
        var warning = canDecide && review.Status == ReviewStatuses.InReview ? await _decisions.GetAcceptWarningAsync(id, ct) : null;
        var status = await _inbox.GetStatusAsync(ct);
        var groupName = review.AssignedGroupId is { } g
            ? await _db.ApproverGroups.AsNoTracking().Where(x => x.Id == g).Select(x => x.Name).FirstOrDefaultAsync(ct)
            : null;

        return new ReviewDetail(row, doc, rules, dte?.Lines ?? Array.Empty<DteReceivedLine>(), events, warning, canDecide,
            await _user.HasActionAsync(BandejaMenu, "EDIT", ct), groupName, review.RecommendedAction, review.Reason,
            review.DecisionSource, status.SupportedActions.Where(DteResponseActions.IsClaim).ToList(), doc.HasPdf || dte is not null, dte);
    }

    public async Task<IReadOnlyList<ReviewRow>> ListMineAsync(CancellationToken ct)
    {
        var mine = await _groups.GetGroupIdsForUserAsync(_user.UserId, ct);
        if (mine.Count == 0) return Array.Empty<ReviewRow>();
        var pending = await _db.InvoiceReviews.AsNoTracking()
            .Where(r => r.CompanyId == CompanyId && r.Status == ReviewStatuses.InReview)
            .OrderBy(r => r.LegalDeadlineOn).ToListAsync(ct);
        var result = new List<FpInvoiceReview>();
        foreach (var r in pending)
        {
            if (r.AssignedGroupId is { } a && mine.Contains(a)) { result.Add(r); continue; }
            var (approver, backup) = await _profiles.GetApproverGroupsAsync(r.IssuerTaxId, r.SiiReceivedOn, ct);
            if ((approver is { } ap && mine.Contains(ap)) || (backup is { } b && mine.Contains(b))) result.Add(r);
        }
        return await ToRowsAsync(result, ct);
    }

    private async Task<Dictionary<FpInvoiceReview, DteReceivedDto>> DocumentsAsync(IReadOnlyList<FpInvoiceReview> reviews, CancellationToken ct)
    {
        var result = new Dictionary<FpInvoiceReview, DteReceivedDto>();
        if (reviews.Count == 0) return result;
        var from = reviews.Min(r => r.SiiReceivedOn).AddDays(-1);
        var byId = (await _inbox.ListAsync(new DteReceivedFilter(from, null, null, 5000), ct)).ToDictionary(d => d.Id);
        foreach (var r in reviews)
        {
            var doc = byId.TryGetValue(r.ReceivedId, out var d) ? d : await _inbox.GetAsync(r.ReceivedId, ct);
            if (doc is not null) result[r] = doc;
        }
        return result;
    }

    private async Task<IReadOnlyList<ReviewRow>> ToRowsAsync(IReadOnlyList<FpInvoiceReview> reviews, CancellationToken ct)
    {
        var docs = await DocumentsAsync(reviews, ct);
        var ids = reviews.Select(r => r.Id).ToList();
        var objections = (await _db.ReviewRuleResults.AsNoTracking()
                .Where(x => ids.Contains(x.ReviewId) && x.Kind == "objection").OrderBy(x => x.Id).ToListAsync(ct))
            .GroupBy(x => x.ReviewId).ToDictionary(g => g.Key, g => g.ToList());

        return reviews.Select(r =>
        {
            docs.TryGetValue(r, out var doc);
            var operatorAction = doc is { ResponseStatus: DteResponseStatuses.Sent or DteResponseStatuses.External } ? doc.ResponseAction : null;
            bool? matches = r.IsSimulated && operatorAction is not null && r.DecidedAction is not null
                ? (r.DecidedAction == RuleActions.Accept) == (operatorAction is DteResponseActions.AcceptContent or DteResponseActions.GoodsReceipt)
                : null;
            var summary = r.Status is ReviewStatuses.Warning or ReviewStatuses.InReview
                ? objections.GetValueOrDefault(r.Id)?.FirstOrDefault(o => o.Action == r.ProposedAction)?.Message ?? r.Reason
                // Las "No admite respuesta" anteriores a guardar el motivo lo toman del documento.
                : r.Reason ?? (r.Status == ReviewStatuses.NotRespondable ? doc?.NotRespondableReason : null);
            return new ReviewRow(r.Id, r.ReceivedId, r.SiiReceivedOn, r.IssuerName, r.IssuerTaxId, r.DteType, r.Folio, r.TotalAmount,
                doc?.PurchaseOrderRefs.Select(p => p.DocNum).Distinct().ToList() ?? new List<int>(), r.Status, summary, r.LegalDeadlineOn,
                r.WarnDeadlineOn, r.DecidedAction, r.IsSimulated, operatorAction, matches, r.ConcurrencyStamp,
                doc?.SiiReceivedAt, doc?.PaymentForm, doc?.DueDate, doc?.HasPdf ?? false, ReferenceLabel(doc));
        }).ToList();
    }

    /// <summary>Documentos tributarios referenciados, ej. "Factura 4045" (la que anula una nota de crédito).</summary>
    private static string? ReferenceLabel(DteReceivedDto? doc)
    {
        if (doc is null) return null;
        var labels = doc.References
            .Select(x => (Ok: int.TryParse(x.Type, NumberStyles.None, CultureInfo.InvariantCulture, out var type), Type: type, x.Folio))
            .Where(x => x.Ok && ReferenceableTypes.Contains(x.Type))
            .Select(x => $"{FacturasProveedorUi.DocumentLabel(x.Type)} {x.Folio}")
            .Distinct().ToList();
        return labels.Count == 0 ? null : string.Join(", ", labels);
    }
}
