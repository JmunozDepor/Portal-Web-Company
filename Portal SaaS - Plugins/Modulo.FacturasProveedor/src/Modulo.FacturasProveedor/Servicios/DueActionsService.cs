using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Data;
using Modulo.FacturasProveedor.Dominio;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Dominio.Reglas.Catalogo;
using Modulo.FacturasProveedor.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.FacturasProveedor.Servicios;

public sealed record DueRunResult(int Sent, int Failed, int Expired, int Escalated);

public interface IDueActionsService
{
    /// <summary>Vencimientos (fin del aviso, regla de plazo, escalamiento, vencidas) y envío de las respuestas por enviar.</summary>
    Task<DueRunResult> RunAsync(CancellationToken ct);
}

/// <summary>
/// Lo que depende del reloj (DueActionPlanner) y el envío al operador. Responde solo las revisiones to_send, con clave
/// estable "fp:{revisión}:{n° de decisión}": un reintento nunca responde dos veces. Una revisión simulated nunca se
/// envía, aunque el modo pase después a activo. Fallas reiteradas o el último día avisan a Finanzas una sola vez.
/// </summary>
public sealed class DueActionsService : IDueActionsService
{
    private const int FailuresBeforeNotice = 3;

    private readonly FacturasProveedorDbContext _db;
    private readonly IDteInboxService _inbox;
    private readonly IModuleSettingsService _settings;
    private readonly InvoiceRuleEngine _engine;
    private readonly ISupplierProfileService _profiles;
    private readonly IApproverGroupService _groups;
    private readonly INotificationService _notifications;
    private readonly ICurrentCompanyAccessor _company;
    private readonly TimeProvider _clock;

    public DueActionsService(FacturasProveedorDbContext db, IDteInboxService inbox, IModuleSettingsService settings,
        InvoiceRuleEngine engine, ISupplierProfileService profiles, IApproverGroupService groups, INotificationService notifications,
        ICurrentCompanyAccessor company, TimeProvider clock)
    {
        _db = db;
        _inbox = inbox;
        _settings = settings;
        _engine = engine;
        _profiles = profiles;
        _groups = groups;
        _notifications = notifications;
        _company = company;
        _clock = clock;
    }

    private Guid CompanyId => _company.CompanyId;

    public async Task<DueRunResult> RunAsync(CancellationToken ct)
    {
        await _settings.GetAsync(ct);
        var settings = await _db.Settings.SingleAsync(s => s.CompanyId == CompanyId, ct);
        var config = await _settings.GetEngineConfigurationAsync(ct);
        var now = _clock.GetUtcNow();
        var nowChile = ChileClock.ToLocal(now.UtcDateTime);
        var simulation = settings.Mode != ModuleModes.Active;
        int sent = 0, failed = 0, expired = 0, escalated = 0;

        var open = await _db.InvoiceReviews
            .Where(r => r.CompanyId == CompanyId && (r.Status == ReviewStatuses.New || r.Status == ReviewStatuses.Warning
                || r.Status == ReviewStatuses.InReview || r.Status == ReviewStatuses.ToSend))
            .OrderBy(r => r.LegalDeadlineOn).ThenBy(r => r.Id)
            .ToListAsync(ct);

        foreach (var review in open)
        {
            var deadlineRule = await DeadlineRuleAsync(review, config, ct);
            var plan = DueActionPlanner.Plan(
                new DueSnapshot(review.Status, review.LegalDeadlineOn, review.WarnDeadlineOn, review.EscalatedAt is not null),
                new DueSettings(settings.CutoffTime, settings.EscalateDaysBefore, deadlineRule.IsActive, deadlineRule.Action,
                    PlazoPorVencerRule.DaysBefore(deadlineRule)),
                nowChile);

            switch (plan.Kind)
            {
                case DueActionKind.MarkExpired:
                    review.Status = ReviewStatuses.Expired;
                    Touch(review, now);
                    AddEvent(review, "expired", "Pasó el plazo legal sin respuesta: la ley la da por aceptada.", now);
                    if (!simulation)
                        await NotifyAsync(settings.FinanceGroupId, review, "Factura vencida sin respuesta",
                            $"{InvoiceReviewService.Describe(review)} pasó el plazo legal sin respuesta: quedó aceptada por ley.",
                            NotificationSeverity.Warning, ct);
                    expired++;
                    break;
                case DueActionKind.RejectWarnExpired:
                    Decide(review, RuleActions.Reject, "rule:oc-referenciada", "No se asoció una OC dentro del aviso.", simulation, now);
                    break;
                case DueActionKind.ApplyDeadlineRule:
                    Decide(review, plan.Action!, $"rule:{PlazoPorVencerRule.RuleCode}", "Plazo por vencer sin decisión.", simulation, now);
                    break;
                case DueActionKind.Escalate:
                    review.EscalatedAt = now;
                    Touch(review, now);
                    AddEvent(review, "escalated", "Escalada a Finanzas por plazo.", now);
                    var message = $"{InvoiceReviewService.Describe(review)} sigue sin decisión y vence el {review.LegalDeadlineOn:dd-MM-yyyy}.";
                    await NotifyAsync(settings.FinanceGroupId, review, "Factura por vencer", message, NotificationSeverity.Warning, ct);
                    if (review.AssignedGroupId != settings.FinanceGroupId)
                        await NotifyAsync(review.AssignedGroupId, review, "Factura por vencer", message, NotificationSeverity.Warning, ct);
                    escalated++;
                    break;
            }
        }
        await _db.SaveChangesAsync(ct);

        foreach (var review in open.Where(r => r.Status == ReviewStatuses.ToSend))
        {
            var outcome = await SendAsync(review, settings, nowChile, now, ct);
            if (outcome == DteRespondOutcome.Sent) sent++;
            if (outcome == DteRespondOutcome.Failed) failed++;
            await _db.SaveChangesAsync(ct);
        }

        return new DueRunResult(sent, failed, expired, escalated);
    }

    private async Task<DteRespondOutcome> SendAsync(FpInvoiceReview review, FpSettings settings, DateTime nowChile, DateTimeOffset now,
        CancellationToken ct)
    {
        var action = review.DecidedAction == RuleActions.Accept
            ? DteResponseActions.AcceptContent
            : review.ClaimType ?? DteResponseActions.ClaimContent;
        var actor = review.DecisionSource == "user" ? $"user:{review.DecidedBy}" : review.DecisionSource ?? "rule:final";
        var result = await _inbox.RespondAsync(
            new DteRespondRequest(review.ReceivedId, action, review.Reason, actor, $"fp:{review.Id}:{review.DecisionNumber}"), ct);

        switch (result.Outcome)
        {
            case DteRespondOutcome.Sent:
                review.Status = review.DecidedAction == RuleActions.Accept ? ReviewStatuses.Accepted : ReviewStatuses.Rejected;
                review.LastError = null;
                AddEvent(review, "sent", $"Respuesta enviada: {Pages.FacturasProveedorUi.ActionLabel(action)}.", now);
                break;
            case DteRespondOutcome.PendingConfirmation:
                review.LastError = result.Error ?? "Pendiente de confirmación del operador.";
                break;
            case DteRespondOutcome.AlreadyResponded:
                review.Status = ReviewStatuses.External;
                AddEvent(review, "external", "El operador ya tenía una respuesta: no se envió.", now);
                break;
            case DteRespondOutcome.NotRespondable:
                review.Status = ReviewStatuses.NotRespondable;
                AddEvent(review, "not_respondable", result.Error ?? "El operador no admite esta respuesta.", now);
                break;
            default:
                review.Attempts++;
                review.LastError = Truncate(result.Error ?? "Falla al responder.", 1000);
                var lastDay = DateOnly.FromDateTime(nowChile) >= review.LegalDeadlineOn;
                if ((review.Attempts >= FailuresBeforeNotice || lastDay)
                    && !await _db.ReviewEvents.AnyAsync(e => e.ReviewId == review.Id && e.EventType == "send_failed_notified", ct))
                {
                    AddEvent(review, "send_failed_notified", review.LastError, now);
                    await NotifyAsync(settings.FinanceGroupId, review, "No se pudo responder al SII",
                        $"{InvoiceReviewService.Describe(review)}: {review.LastError} Vence el {review.LegalDeadlineOn:dd-MM-yyyy}.",
                        NotificationSeverity.Danger, ct);
                }
                break;
        }
        Touch(review, now);
        return result.Outcome;
    }

    /// <summary>Regla "plazo por vencer" ya resuelta con la excepción del proveedor.</summary>
    private async Task<EffectiveRuleSettings> DeadlineRuleAsync(FpInvoiceReview review, CompanyRuleConfiguration config, CancellationToken ct)
    {
        var profile = await _profiles.GetActiveFactsAsync(review.IssuerTaxId, review.SiiReceivedOn, ct);
        return RuleSettingsResolver.Resolve(config, profile, _engine.Catalog).ByRule[PlazoPorVencerRule.RuleCode];
    }

    private void Decide(FpInvoiceReview review, string action, string source, string reason, bool simulation, DateTimeOffset now)
    {
        review.Status = simulation ? ReviewStatuses.Simulated : ReviewStatuses.ToSend;
        review.IsSimulated = simulation;
        review.DecidedAction = action;
        review.DecisionSource = source;
        review.DecidedAt = now;
        review.Reason = reason;
        review.DecisionNumber++;
        Touch(review, now);
        AddEvent(review, "rule_deadline", $"{Pages.FacturasProveedorUi.ActionLabel(action)} por regla: {reason}", now);
    }

    private async Task NotifyAsync(long? groupId, FpInvoiceReview review, string title, string message, string severity, CancellationToken ct)
    {
        if (groupId is not { } id) return;
        foreach (var userId in await _groups.GetMemberIdsAsync(id, ct))
            await _notifications.TryEnqueueAsync(userId, title, message, severity, $"/facturas-proveedor/bandeja/{review.Id}", ct);
    }

    private void AddEvent(FpInvoiceReview review, string type, string detail, DateTimeOffset now) =>
        _db.ReviewEvents.Add(new FpReviewEvent { ReviewId = review.Id, OccurredAt = now, EventType = type, Detail = Truncate(detail, 1000) });

    private static void Touch(FpInvoiceReview review, DateTimeOffset now)
    {
        review.UpdatedAt = now;
        review.ConcurrencyStamp = Guid.NewGuid();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
