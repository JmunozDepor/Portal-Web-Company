using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Models;
using Modulo.FacturasProveedor.Servicios;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.FacturasProveedor.Tests.Servicios;

public class ReviewDecisionServiceTests
{
    private static ReviewDecisionService Decisions(ReviewTestEnv env) =>
        new(env.Db, env.Inbox, env.Sap, env.Profiles, env.Groups, env.Reviews, env.Engine, env.Settings, env.User, env.Company, env.Clock);

    private static Task<FpInvoiceReview> ReviewAsync(ReviewTestEnv env, long receivedId) =>
        env.Db.InvoiceReviews.SingleAsync(r => r.ReceivedId == receivedId);

    /// <summary>Factura de gasto fijo en revisión, asignada a un grupo con approver.</summary>
    private static async Task<(FpInvoiceReview Review, Guid Approver)> InReviewAsync(ReviewTestEnv env)
    {
        var approver = Guid.NewGuid();
        var group = await env.Groups.SaveAsync(new ApproverGroupInput(null, "Gerencia", false, true, new[] { approver }), CancellationToken.None);
        await env.Profiles.SaveAsync(new SupplierProfileInput(null, "77555010-K", SupplierConditions.FixedMonthly, group, null,
            new DateOnly(2026, 10, 1), null, null, true, Array.Empty<RuleOverrideInput>()), CancellationToken.None);
        var doc = env.Invoice(taxId: "77555010-K", purchaseOrders: Array.Empty<int>(), total: 4_100_000);
        await env.EvaluateAsync();
        return (await ReviewAsync(env, doc.Id), approver);
    }

    [Fact]
    public async Task Decide_NoIntegrante_Falla()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var (review, _) = await InReviewAsync(env);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Decisions(env).DecideAsync(review.Id, RuleActions.Accept, null, null, review.ConcurrencyStamp, CancellationToken.None));
        Assert.False(await Decisions(env).CanDecideAsync(review.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Decide_Aprobador_Acepta()
    {
        var env = await new ReviewTestEnv().StartAsync(ModuleModes.Active);
        var (review, approver) = await InReviewAsync(env);
        env.User.UserId = approver;

        await Decisions(env).DecideAsync(review.Id, RuleActions.Accept, null, null, review.ConcurrencyStamp, CancellationToken.None);

        var saved = await env.Db.InvoiceReviews.SingleAsync(r => r.Id == review.Id);
        Assert.Equal(ReviewStatuses.ToSend, saved.Status);
        Assert.Equal("user", saved.DecisionSource);
        Assert.Equal(approver, saved.DecidedBy);
        Assert.Equal(1, saved.DecisionNumber);
    }

    [Fact]
    public async Task Decide_Finanzas_PuedeDecidirAviso()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();
        var review = await ReviewAsync(env, doc.Id);
        Assert.Equal(ReviewStatuses.Warning, review.Status);
        env.User.UserId = env.FinanceUser;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Decisions(env).DecideAsync(review.Id, RuleActions.Reject, "No corresponde a ninguna compra", null, review.ConcurrencyStamp, CancellationToken.None));

        env.User.Grant("FacturasProveedor.bandeja", "EDIT");
        await Decisions(env).DecideAsync(review.Id, RuleActions.Reject, "No corresponde a ninguna compra", null, review.ConcurrencyStamp, CancellationToken.None);
        Assert.Equal(RuleActions.Reject, (await ReviewAsync(env, doc.Id)).DecidedAction);
    }

    [Fact]
    public async Task Decide_RechazoSinMotivo_Falla()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var (review, approver) = await InReviewAsync(env);
        env.User.UserId = approver;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Decisions(env).DecideAsync(review.Id, RuleActions.Reject, "  no ", null, review.ConcurrencyStamp, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Decisions(env).DecideAsync(review.Id, RuleActions.Reject, "Monto no corresponde", DteResponseActions.GoodsReceipt, review.ConcurrencyStamp, CancellationToken.None));
    }

    [Fact]
    public async Task Decide_Concurrencia_SegundoFalla()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var (review, approver) = await InReviewAsync(env);
        env.User.UserId = approver;
        var stamp = review.ConcurrencyStamp;

        await Decisions(env).DecideAsync(review.Id, RuleActions.Accept, null, null, stamp, CancellationToken.None);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Decisions(env).DecideAsync(review.Id, RuleActions.Reject, "Monto no corresponde", null, stamp, CancellationToken.None));

        Assert.Contains("Otra persona", ex.Message);
        Assert.Equal(RuleActions.Accept, (await env.Db.InvoiceReviews.SingleAsync(r => r.Id == review.Id)).DecidedAction);
    }

    [Fact]
    public async Task Decide_Simulacion_QuedaSimulada()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var (review, approver) = await InReviewAsync(env);
        env.User.UserId = approver;

        await Decisions(env).DecideAsync(review.Id, RuleActions.Reject, "Monto no corresponde al contrato", DteResponseActions.ClaimPartialGoods,
            review.ConcurrencyStamp, CancellationToken.None);

        var saved = await env.Db.InvoiceReviews.SingleAsync(r => r.Id == review.Id);
        Assert.Equal(ReviewStatuses.Simulated, saved.Status);
        Assert.True(saved.IsSimulated);
        Assert.Equal(DteResponseActions.ClaimPartialGoods, saved.ClaimType);
        Assert.Empty(env.Inbox.Responses);
    }

    [Fact]
    public async Task AcceptWarning_SuperaSaldo_Mensaje()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder(total: 3_200_000);
        var approver = Guid.NewGuid();
        var group = await env.Groups.SaveAsync(new ApproverGroupInput(null, "Compras", false, true, new[] { approver }), CancellationToken.None);
        await env.Profiles.SaveAsync(new SupplierProfileInput(null, "76412553-1", SupplierConditions.AlwaysManual, group, null,
            new DateOnly(2026, 10, 1), null, null, true, Array.Empty<RuleOverrideInput>()), CancellationToken.None);
        var doc = env.Invoice(total: 3_380_000);
        await env.EvaluateAsync();
        var review = await ReviewAsync(env, doc.Id);

        var warning = await Decisions(env).GetAcceptWarningAsync(review.Id, CancellationToken.None);

        Assert.Equal(ReviewStatuses.InReview, review.Status);
        Assert.Equal("Aceptarla supera el saldo de la OC 4512 en $180.000.", warning);
    }

    [Fact]
    public async Task Associate_OcDeOtroProveedor_Falla()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder(docNum: 9999, taxId: "77555010-K", cardCode: "P77555010");
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();
        env.User.Grant("FacturasProveedor.bandeja", "EDIT");

        var reviewId = (await ReviewAsync(env, doc.Id)).Id;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Decisions(env).AssociatePurchaseOrderAsync(reviewId, 9999, CancellationToken.None));
        Assert.Contains("9999", ex.Message);
        Assert.Empty(env.Inbox.ManualRefs);
    }

    [Fact]
    public async Task Associate_SinPermiso_Falla()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder();
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();

        var reviewId = (await ReviewAsync(env, doc.Id)).Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Decisions(env).AssociatePurchaseOrderAsync(reviewId, 4512, CancellationToken.None));
    }

    [Fact]
    public async Task Associate_Valida_Reevalua()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder();
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();
        env.User.Grant("FacturasProveedor.bandeja", "EDIT");
        var review = await ReviewAsync(env, doc.Id);

        await Decisions(env).AssociatePurchaseOrderAsync(review.Id, 4512, CancellationToken.None);

        Assert.Equal(new[] { 4512 }, env.Inbox.ManualRefs.Single().DocNums);
        var saved = await env.Db.InvoiceReviews.SingleAsync(r => r.Id == review.Id);
        Assert.Equal(ReviewStatuses.Simulated, saved.Status);
        Assert.Contains(await env.Db.ReviewEvents.Where(e => e.ReviewId == review.Id).ToListAsync(), e => e.EventType == "po_associated");
    }

    [Fact]
    public async Task Associate_EnAceptada_SoloActualizaRefs()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder().AddPurchaseOrder(docNum: 4530);
        var doc = env.Invoice();
        await env.EvaluateAsync();
        var review = await ReviewAsync(env, doc.Id);
        review.Status = ReviewStatuses.Accepted;
        await env.Db.SaveChangesAsync();
        env.User.Grant("FacturasProveedor.bandeja", "EDIT");

        await Decisions(env).AssociatePurchaseOrderAsync(review.Id, 4530, CancellationToken.None);

        Assert.Single(env.Inbox.ManualRefs);
        Assert.Equal(ReviewStatuses.Accepted, (await env.Db.InvoiceReviews.SingleAsync(r => r.Id == review.Id)).Status);
    }
}
