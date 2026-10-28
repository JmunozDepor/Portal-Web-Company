using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Data;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Models;
using Modulo.FacturasProveedor.Pages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.FacturasProveedor.Servicios;

public interface IReviewDecisionService
{
    Task<bool> CanDecideAsync(long reviewId, CancellationToken ct);

    /// <summary>Acepta o rechaza a mano (action = accept/reject). concurrencyStamp = el que vio el usuario.</summary>
    Task DecideAsync(long reviewId, string action, string? reason, string? claimType, Guid concurrencyStamp, CancellationToken ct);

    /// <summary>Advertencia no bloqueante si aceptar supera el saldo de la OC; null si no.</summary>
    Task<string?> GetAcceptWarningAsync(long reviewId, CancellationToken ct);

    /// <summary>Asocia a mano una OC (DocNum) del mismo proveedor y reevalúa si la revisión sigue pendiente.</summary>
    Task AssociatePurchaseOrderAsync(long reviewId, int docNum, CancellationToken ct);
}

/// <summary>
/// Decisiones de las personas (spec §9): decide un integrante del grupo asignado o del suplente del proveedor; Finanzas
/// (integrante del grupo de Finanzas con EDIT en la bandeja) puede decidir cualquier factura pendiente, incluso en aviso.
/// Rechazar exige motivo y un tipo de reclamo que el operador admita. Si dos personas deciden a la vez, gana la primera.
/// </summary>
public sealed class ReviewDecisionService : IReviewDecisionService
{
    private const string BandejaMenu = "FacturasProveedor.bandeja";

    private readonly FacturasProveedorDbContext _db;
    private readonly IDteInboxService _inbox;
    private readonly ISapLookupService _sap;
    private readonly ISupplierProfileService _profiles;
    private readonly IApproverGroupService _groups;
    private readonly IInvoiceReviewService _reviews;
    private readonly InvoiceRuleEngine _engine;
    private readonly IModuleSettingsService _settings;
    private readonly ICurrentUserContext _user;
    private readonly ICurrentCompanyAccessor _company;
    private readonly TimeProvider _clock;

    public ReviewDecisionService(FacturasProveedorDbContext db, IDteInboxService inbox, ISapLookupService sap,
        ISupplierProfileService profiles, IApproverGroupService groups, IInvoiceReviewService reviews, InvoiceRuleEngine engine,
        IModuleSettingsService settings, ICurrentUserContext user, ICurrentCompanyAccessor company, TimeProvider clock)
    {
        _db = db;
        _inbox = inbox;
        _sap = sap;
        _profiles = profiles;
        _groups = groups;
        _reviews = reviews;
        _engine = engine;
        _settings = settings;
        _user = user;
        _company = company;
        _clock = clock;
    }

    private Guid CompanyId => _company.CompanyId;

    public async Task<bool> CanDecideAsync(long reviewId, CancellationToken ct)
    {
        var review = await FindAsync(reviewId, ct);
        return review is not null && await CanDecideAsync(review, ct);
    }

    public async Task DecideAsync(long reviewId, string action, string? reason, string? claimType, Guid concurrencyStamp, CancellationToken ct)
    {
        var review = await FindAsync(reviewId, ct) ?? throw new InvalidOperationException("La factura ya no existe en la bandeja.");
        if (review.ConcurrencyStamp != concurrencyStamp)
            throw new InvalidOperationException("Otra persona ya decidió esta factura. Recargue la página para ver cómo quedó.");
        var finance = await IsFinanceAsync(ct);
        if (review.Status != ReviewStatuses.InReview && !(review.Status == ReviewStatuses.Warning && finance))
            throw new InvalidOperationException("La factura ya no está pendiente de decisión.");
        if (!await CanDecideAsync(review, ct))
            throw new InvalidOperationException("No tienes permiso para decidir esta factura.");
        if (action is not (RuleActions.Accept or RuleActions.Reject))
            throw new InvalidOperationException("La decisión debe ser aceptar o rechazar.");

        string? claim = null;
        if (action == RuleActions.Reject)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
                throw new InvalidOperationException("Indique el motivo del rechazo (al menos 5 caracteres).");
            claim = claimType ?? DteResponseActions.ClaimContent;
            var status = await _inbox.GetStatusAsync(ct);
            if (!DteResponseActions.IsClaim(claim) || !status.SupportedActions.Contains(claim))
                throw new InvalidOperationException("El tipo de reclamo elegido no está disponible con el operador de la compañía.");
        }

        var settings = await _settings.GetAsync(ct);
        var simulation = settings.General.Mode != ModuleModes.Active;
        var now = _clock.GetUtcNow();
        _db.Entry(review).Property(r => r.ConcurrencyStamp).OriginalValue = concurrencyStamp;
        review.Status = simulation ? ReviewStatuses.Simulated : ReviewStatuses.ToSend;
        review.IsSimulated = simulation;
        review.DecidedAction = action;
        review.DecisionSource = "user";
        review.DecidedBy = _user.UserId;
        review.DecidedAt = now;
        review.Reason = string.IsNullOrWhiteSpace(reason) ? null : Truncate(reason.Trim(), 500);
        review.ClaimType = claim;
        review.DecisionNumber++;
        review.UpdatedAt = now;
        review.ConcurrencyStamp = Guid.NewGuid();
        _db.ReviewEvents.Add(new FpReviewEvent
        {
            ReviewId = review.Id, OccurredAt = now, EventType = "decided", UserId = _user.UserId,
            Detail = Truncate($"{FacturasProveedorUi.ActionLabel(action)}" + (review.Reason is { } r ? $": {r}" : ""), 1000),
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Otra persona ya decidió esta factura. Recargue la página para ver cómo quedó.");
        }
    }

    public async Task<string?> GetAcceptWarningAsync(long reviewId, CancellationToken ct)
    {
        var ctx = await _reviews.BuildContextAsync(reviewId, ct);
        if (ctx is null || ctx.PurchaseOrders.Count == 0) return null;
        var rule = _engine.Find("oc-saldo");
        var config = await _settings.GetEngineConfigurationAsync(ct);
        if (rule is null || !config.ByRule.TryGetValue(rule.Code, out var settings)) return null;
        var outcome = rule.Evaluate(ctx, settings);
        if (outcome.Kind != RuleOutcomeKind.Objection || outcome.DataJson is null) return null;

        using var data = JsonDocument.Parse(outcome.DataJson);
        var excess = data.RootElement.GetProperty("invoiceTotal").GetDecimal() - data.RootElement.GetProperty("balance").GetDecimal();
        var docNums = string.Join(", ", ctx.PurchaseOrders.Select(po => po.DocNum.ToString(CultureInfo.InvariantCulture)));
        return $"Aceptarla supera el saldo de la OC {docNums} en ${FacturasProveedorUi.Amount(excess)}.";
    }

    public async Task AssociatePurchaseOrderAsync(long reviewId, int docNum, CancellationToken ct)
    {
        if (!await _user.HasActionAsync(BandejaMenu, "EDIT", ct))
            throw new InvalidOperationException("No tienes permiso para asociar órdenes de compra.");
        var review = await FindAsync(reviewId, ct) ?? throw new InvalidOperationException("La factura ya no existe en la bandeja.");
        if (docNum <= 0) throw new InvalidOperationException("Ingrese el número (DocNum) de la OC.");

        var supplier = await _sap.FindSupplierByTaxIdAsync(review.IssuerTaxId, ct);
        var issuer = SapLookupService.NormalizeRut(review.IssuerTaxId);
        var match = (await _sap.FindPurchaseOrdersAsync(docNum, ct)).Any(po =>
            po.SupplierTaxId is { Length: > 0 } rut ? SapLookupService.NormalizeRut(rut) == issuer : po.CardCode == supplier?.CardCode);
        if (!match)
            throw new InvalidOperationException($"La OC {docNum} no existe en SAP o es de otro proveedor.");

        var doc = await _inbox.GetAsync(review.ReceivedId, ct)
            ?? throw new InvalidOperationException("La factura ya no está en la bandeja de recibidos.");
        var manual = doc.PurchaseOrderRefs.Where(r => r.Source == DtePurchaseOrderRefSources.Manual).Select(r => r.DocNum)
            .Append(docNum).Distinct().ToList();
        await _inbox.SetPurchaseOrderRefsAsync(review.ReceivedId, manual, _user.UserId, ct);

        _db.ReviewEvents.Add(new FpReviewEvent
        {
            ReviewId = review.Id, OccurredAt = _clock.GetUtcNow(), EventType = "po_associated", UserId = _user.UserId,
            Detail = $"OC {docNum} asociada a mano.",
        });
        await _db.SaveChangesAsync(ct);

        if (review.Status is ReviewStatuses.New or ReviewStatuses.Warning or ReviewStatuses.InReview && review.DecidedBy is null)
            await _reviews.EvaluateOneAsync(review.Id, ct);
    }

    private async Task<bool> CanDecideAsync(FpInvoiceReview review, CancellationToken ct)
    {
        var mine = await _groups.GetGroupIdsForUserAsync(_user.UserId, ct);
        if (review.AssignedGroupId is { } assigned && mine.Contains(assigned)) return true;
        var (approver, backup) = await _profiles.GetApproverGroupsAsync(review.IssuerTaxId, review.SiiReceivedOn, ct);
        if ((approver is { } a && mine.Contains(a)) || (backup is { } b && mine.Contains(b))) return true;
        return await IsFinanceAsync(ct);
    }

    /// <summary>Integrante del grupo de Finanzas con permiso de edición en la bandeja.</summary>
    private async Task<bool> IsFinanceAsync(CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        if (settings.General.FinanceGroupId is not { } finance) return false;
        var mine = await _groups.GetGroupIdsForUserAsync(_user.UserId, ct);
        return mine.Contains(finance) && await _user.HasActionAsync(BandejaMenu, "EDIT", ct);
    }

    private Task<FpInvoiceReview?> FindAsync(long reviewId, CancellationToken ct) =>
        _db.InvoiceReviews.FirstOrDefaultAsync(r => r.CompanyId == CompanyId && r.Id == reviewId, ct);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
