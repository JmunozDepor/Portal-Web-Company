using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Data;
using Modulo.FacturasProveedor.Dominio;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Models;
using Modulo.FacturasProveedor.Pages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.FacturasProveedor.Servicios;

public sealed record ReviewCycleResult(int Created, int Evaluated, int Changed);

public interface IInvoiceReviewService
{
    /// <summary>Trae los cambios de la bandeja de plataforma y evalúa las facturas pendientes de la compañía activa.</summary>
    Task<ReviewCycleResult> EvaluatePendingAsync(CancellationToken ct);

    /// <summary>Igual, pero evalúa solo una revisión (ej. después de asociar una OC).</summary>
    Task EvaluateOneAsync(long reviewId, CancellationToken ct);
}

/// <summary>
/// Orquestador de la evaluación (spec §7 y §8): crea una revisión por cada factura respondible de la bandeja, la deja
/// fuera de alcance si se recibió antes de "procesar desde", y pasa las pendientes por el motor de reglas en orden de
/// recepción. Nunca responde al SII: deja la decisión en to_send (modo activo) o simulated (modo simulación); el envío
/// es de DueActionsService.
/// </summary>
public sealed class InvoiceReviewService : IInvoiceReviewService
{
    private const int PageSize = 500;

    private readonly FacturasProveedorDbContext _db;
    private readonly IDteInboxService _inbox;
    private readonly InvoiceRuleEngine _engine;
    private readonly IModuleSettingsService _settings;
    private readonly ISapLookupService _sap;
    private readonly ISupplierProfileService _profiles;
    private readonly IApproverGroupService _groups;
    private readonly INotificationService _notifications;
    private readonly ICurrentCompanyAccessor _company;
    private readonly TimeProvider _clock;

    public InvoiceReviewService(FacturasProveedorDbContext db, IDteInboxService inbox, InvoiceRuleEngine engine,
        IModuleSettingsService settings, ISapLookupService sap, ISupplierProfileService profiles, IApproverGroupService groups,
        INotificationService notifications, ICurrentCompanyAccessor company, TimeProvider clock)
    {
        _db = db;
        _inbox = inbox;
        _engine = engine;
        _settings = settings;
        _sap = sap;
        _profiles = profiles;
        _groups = groups;
        _notifications = notifications;
        _company = company;
        _clock = clock;
    }

    private Guid CompanyId => _company.CompanyId;

    public Task<ReviewCycleResult> EvaluatePendingAsync(CancellationToken ct) => RunAsync(null, ct);

    public Task EvaluateOneAsync(long reviewId, CancellationToken ct) => RunAsync(reviewId, ct);

    private async Task<ReviewCycleResult> RunAsync(long? onlyReviewId, CancellationToken ct)
    {
        await _settings.GetAsync(ct); // crea los valores por defecto la primera vez
        var settings = await _db.Settings.SingleAsync(s => s.CompanyId == CompanyId, ct);
        var status = await _inbox.GetStatusAsync(ct);
        var now = _clock.GetUtcNow();
        var today = ChileToday(now);
        var simulation = settings.Mode != ModuleModes.Active;

        var created = await PullChangesAsync(settings, status, now, simulation, ct);
        await RescopeAsync(settings, today, ct);
        await _db.SaveChangesAsync(ct);

        var config = await _settings.GetEngineConfigurationAsync(ct);
        var maxDays = config.ByRule.TryGetValue("oc-antiguedad", out var age) ? (int)age.Param("max_days", 30) : 30;
        var windowDays = Math.Max(60, maxDays + settings.LegalDeadlineDays);
        var window = await _inbox.ListAsync(new DteReceivedFilter(today.AddDays(-windowDays), null, null, 5000), ct);
        var docsById = window.ToDictionary(d => d.Id);

        var pending = await _db.InvoiceReviews
            .Where(r => r.CompanyId == CompanyId && r.DecidedBy == null
                && (r.Status == ReviewStatuses.New || r.Status == ReviewStatuses.Warning || r.Status == ReviewStatuses.InReview))
            .Where(r => onlyReviewId == null || r.Id == onlyReviewId)
            .OrderBy(r => r.SiiReceivedOn).ThenBy(r => r.ReceivedId)
            .ToListAsync(ct);

        var acceptedIds = await _db.InvoiceReviews
            .Where(r => r.CompanyId == CompanyId && r.DecidedAction == RuleActions.Accept
                && (r.Status == ReviewStatuses.ToSend || r.Status == ReviewStatuses.Simulated || r.Status == ReviewStatuses.Accepted))
            .Select(r => r.ReceivedId).ToListAsync(ct);
        var alreadyAccepted = window.Where(d => CountsAsAccepted(d, settings.LegalDeadlineDays, today) || acceptedIds.Contains(d.Id));
        var builder = new ReviewContextBuilder(_sap, _profiles, alreadyAccepted);

        var changed = 0;
        foreach (var review in pending)
        {
            var doc = docsById.TryGetValue(review.ReceivedId, out var d) ? d : await _inbox.GetAsync(review.ReceivedId, ct);
            if (doc is null) continue;
            var ctx = await builder.BuildAsync(doc, review.SiiReceivedOn, ct);
            var decision = _engine.Decide(ctx, config);
            if (await ApplyAsync(review, decision, settings, simulation, now, ct)) changed++;
            if (review.DecidedAction == RuleActions.Accept) builder.MarkAccepted(doc);
        }

        await _db.SaveChangesAsync(ct);
        return new ReviewCycleResult(created, pending.Count, changed);
    }

    /// <summary>Lee la bandeja desde el cursor: crea revisiones nuevas y registra respuestas confirmadas o externas.</summary>
    private async Task<int> PullChangesAsync(FpSettings settings, DteInboxStatus status, DateTimeOffset now, bool simulation, CancellationToken ct)
    {
        var created = 0;
        while (true)
        {
            var page = await _inbox.ListChangedSinceAsync(settings.InboxCursor ?? DateTimeOffset.MinValue, settings.InboxCursorId, PageSize, ct);
            foreach (var doc in page)
            {
                settings.InboxCursor = doc.UpdatedAt;
                settings.InboxCursorId = doc.Id;
                if (!status.RespondableTypes.Contains(doc.DteType)) continue;

                var review = await _db.InvoiceReviews.FirstOrDefaultAsync(r => r.CompanyId == CompanyId && r.ReceivedId == doc.Id, ct)
                    ?? _db.InvoiceReviews.Local.FirstOrDefault(r => r.CompanyId == CompanyId && r.ReceivedId == doc.Id);
                if (review is null)
                {
                    review = NewReview(doc, settings, now);
                    _db.InvoiceReviews.Add(review);
                    created++;
                }
                else
                {
                    review.IssuerName = Truncate(doc.IssuerName, 100);
                    review.TotalAmount = doc.TotalAmount;
                }
                await ApplyInboxResponseAsync(review, doc, settings, simulation, now, ct);
            }
            if (page.Count < PageSize) return created;
        }
    }

    private FpInvoiceReview NewReview(DteReceivedDto doc, FpSettings settings, DateTimeOffset now)
    {
        var received = doc.SiiReceivedAt is { } at ? ChileToday(at) : doc.IssueDate;
        var inScope = settings.ProcessFromDate is { } from && received >= from;
        return new FpInvoiceReview
        {
            CompanyId = CompanyId, ReceivedId = doc.Id, IssuerTaxId = doc.IssuerTaxId, IssuerName = Truncate(doc.IssuerName, 100),
            DteType = doc.DteType, Folio = doc.Folio, TotalAmount = doc.TotalAmount, SiiReceivedOn = received,
            LegalDeadlineOn = ReviewDeadlines.LegalDeadline(received, settings.LegalDeadlineDays),
            Status = inScope ? ReviewStatuses.New : ReviewStatuses.OutOfScope, CreatedAt = now, UpdatedAt = now,
        };
    }

    /// <summary>La plataforma confirmó nuestra respuesta, o alguien respondió por fuera del portal.</summary>
    private async Task ApplyInboxResponseAsync(FpInvoiceReview review, DteReceivedDto doc, FpSettings settings, bool simulation,
        DateTimeOffset now, CancellationToken ct)
    {
        if (doc.ResponseStatus == DteResponseStatuses.Sent && review.Status == ReviewStatuses.ToSend)
        {
            review.Status = review.DecidedAction == RuleActions.Accept ? ReviewStatuses.Accepted : ReviewStatuses.Rejected;
            Touch(review, now);
            await AddEventAsync(review, "sent_confirmed", null, "Respuesta confirmada por el operador.", now, ct);
        }
        else if (doc.ResponseStatus == DteResponseStatuses.External && review.Status != ReviewStatuses.External
                 && (!ReviewStatuses.IsFinal(review.Status) || review.Status == ReviewStatuses.OutOfScope))
        {
            var wasActive = review.Status != ReviewStatuses.OutOfScope;
            review.Status = ReviewStatuses.External;
            Touch(review, now);
            await AddEventAsync(review, "external", null, $"Respondida fuera del portal: {FacturasProveedorUi.ActionLabel(doc.ResponseAction)}.", now, ct);
            if (!simulation && wasActive)
            {
                await NotifyGroupAsync(settings.FinanceGroupId, review, "Factura respondida fuera del portal",
                    $"{Describe(review)} se respondió fuera del portal ({FacturasProveedorUi.ActionLabel(doc.ResponseAction)}).",
                    NotificationSeverity.Warning, ct);
            }
        }
    }

    /// <summary>Si "procesar desde" se fijó o adelantó, las fuera de alcance desde esa fecha y aún en plazo entran al flujo.</summary>
    private async Task RescopeAsync(FpSettings settings, DateOnly today, CancellationToken ct)
    {
        if (settings.ProcessFromDate is not { } from) return;
        var candidates = await _db.InvoiceReviews
            .Where(r => r.CompanyId == CompanyId && r.Status == ReviewStatuses.OutOfScope && r.SiiReceivedOn >= from && r.LegalDeadlineOn >= today)
            .ToListAsync(ct);
        candidates.AddRange(_db.InvoiceReviews.Local.Where(r => r.Status == ReviewStatuses.OutOfScope && r.SiiReceivedOn >= from
            && r.LegalDeadlineOn >= today && !candidates.Contains(r)));
        foreach (var review in candidates) review.Status = ReviewStatuses.New;
    }

    /// <summary>Aplica la decisión del motor. Devuelve true si cambió la acción resultante.</summary>
    private async Task<bool> ApplyAsync(FpInvoiceReview review, InvoiceDecision decision, FpSettings settings, bool simulation,
        DateTimeOffset now, CancellationToken ct)
    {
        var changed = review.ProposedAction != decision.Action || review.RecommendedAction != decision.RecommendedAction;
        var previousStatus = review.Status;
        review.ProposedAction = decision.Action;
        review.RecommendedAction = decision.RecommendedAction;
        review.EvaluationCount++;
        review.LastEvaluatedAt = now;

        switch (decision.Action)
        {
            case RuleActions.Accept or RuleActions.Reject:
                review.Status = simulation ? ReviewStatuses.Simulated : ReviewStatuses.ToSend;
                review.IsSimulated = simulation;
                review.DecidedAction = decision.Action;
                review.DecisionSource = decision.WinningRuleCode is { } code ? $"rule:{code}" : "rule:final";
                review.DecidedAt = now;
                review.Reason = Truncate(decision.Message ?? (decision.Action == RuleActions.Accept ? "Cumple todas las reglas." : null), 500);
                review.DecisionNumber++;
                break;
            case RuleActions.WarnThenReject:
                review.Status = ReviewStatuses.Warning;
                review.WarnDeadlineOn ??= ReviewDeadlines.WarnDeadline(review.SiiReceivedOn, decision.WarnDays ?? 3, review.LegalDeadlineOn);
                if (previousStatus != ReviewStatuses.Warning)
                {
                    await NotifyGroupAsync(settings.FinanceGroupId, review, "Factura sin OC",
                        $"{Describe(review)} sin OC: se rechaza el {review.WarnDeadlineOn:dd-MM-yyyy} si no se asocia.",
                        NotificationSeverity.Warning, ct);
                }
                break;
            default:
                review.Status = ReviewStatuses.InReview;
                var (approver, _) = await _profiles.GetApproverGroupsAsync(review.IssuerTaxId, review.SiiReceivedOn, ct);
                review.AssignedGroupId = approver ?? settings.FinanceGroupId;
                if (previousStatus != ReviewStatuses.InReview)
                {
                    await NotifyGroupAsync(review.AssignedGroupId, review, "Factura por decidir",
                        $"{Describe(review)} requiere tu revisión. Vence el {review.LegalDeadlineOn:dd-MM-yyyy}.",
                        NotificationSeverity.Info, ct);
                }
                break;
        }

        await ReplaceRuleResultsAsync(review, decision, ct);
        if (changed) await AddEventAsync(review, "evaluated", null, $"Reglas evaluadas: {FacturasProveedorUi.ActionLabel(decision.Action)}"
            + (decision.Message is { } m ? $" — {m}" : ""), now, ct);
        Touch(review, now);
        return changed;
    }

    private async Task ReplaceRuleResultsAsync(FpInvoiceReview review, InvoiceDecision decision, CancellationToken ct)
    {
        if (review.Id != 0)
            _db.ReviewRuleResults.RemoveRange(await _db.ReviewRuleResults.Where(r => r.ReviewId == review.Id).ToListAsync(ct));
        else
            await _db.SaveChangesAsync(ct); // necesita Id para colgar los resultados
        foreach (var r in decision.Results)
        {
            _db.ReviewRuleResults.Add(new FpReviewRuleResult
            {
                ReviewId = review.Id, RuleCode = r.RuleCode, Kind = KindText(r.Kind), Action = r.Action,
                Message = r.Message is null ? null : Truncate(r.Message, 500), DataJson = r.DataJson,
            });
        }
    }

    /// <summary>Aceptada por el operador (por el portal o por fuera) o tácitamente: pasó el plazo legal sin reclamo.</summary>
    private static bool CountsAsAccepted(DteReceivedDto doc, int legalDays, DateOnly today)
    {
        if (doc.ResponseStatus is DteResponseStatuses.Sent or DteResponseStatuses.External)
            return doc.ResponseAction is DteResponseActions.AcceptContent or DteResponseActions.GoodsReceipt;
        var received = doc.SiiReceivedAt is { } at ? ChileToday(at) : doc.IssueDate;
        return doc.ResponseStatus == DteResponseStatuses.None && ReviewDeadlines.LegalDeadline(received, legalDays) < today;
    }

    private async Task NotifyGroupAsync(long? groupId, FpInvoiceReview review, string title, string message, string severity, CancellationToken ct)
    {
        if (groupId is not { } id) return;
        if (review.Id == 0) await _db.SaveChangesAsync(ct); // la URL lleva el Id de la revisión
        foreach (var userId in await _groups.GetMemberIdsAsync(id, ct))
        {
            await _notifications.TryEnqueueAsync(userId, title, message, severity, $"/facturas-proveedor/bandeja/{review.Id}", ct);
        }
    }

    private async Task AddEventAsync(FpInvoiceReview review, string type, Guid? userId, string detail, DateTimeOffset now, CancellationToken ct)
    {
        if (review.Id == 0) await _db.SaveChangesAsync(ct);
        _db.ReviewEvents.Add(new FpReviewEvent { ReviewId = review.Id, OccurredAt = now, EventType = type, UserId = userId, Detail = Truncate(detail, 1000) });
    }

    private static void Touch(FpInvoiceReview review, DateTimeOffset now)
    {
        review.UpdatedAt = now;
        review.ConcurrencyStamp = Guid.NewGuid();
    }

    internal static string Describe(FpInvoiceReview r) =>
        $"{FacturasProveedorUi.DocumentLabel(r.DteType)} {r.Folio.ToString(CultureInfo.InvariantCulture)} de {r.IssuerName}";

    internal static DateOnly ChileToday(DateTimeOffset instant) => DateOnly.FromDateTime(ChileClock.ToLocal(instant.UtcDateTime));

    private static string KindText(RuleOutcomeKind kind) => kind switch
    {
        RuleOutcomeKind.Pass => "pass",
        RuleOutcomeKind.Objection => "objection",
        _ => "not_applicable",
    };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
