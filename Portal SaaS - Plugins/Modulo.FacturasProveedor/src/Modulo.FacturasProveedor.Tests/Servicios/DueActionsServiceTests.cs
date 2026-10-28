using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Models;
using Modulo.FacturasProveedor.Servicios;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.FacturasProveedor.Tests.Servicios;

public class DueActionsServiceTests
{
    private static DueActionsService Due(ReviewTestEnv env) =>
        new(env.Db, env.Inbox, env.Settings, env.Engine, env.Profiles, env.Groups, env.Notifications, env.Company, env.Clock);

    private static Task<FpInvoiceReview> ReviewAsync(ReviewTestEnv env, long receivedId) =>
        env.Db.InvoiceReviews.SingleAsync(r => r.ReceivedId == receivedId);

    private static async Task<long> FixedMonthlyAsync(ReviewTestEnv env, IReadOnlyList<RuleOverrideInput>? overrides = null)
    {
        var group = await env.Groups.SaveAsync(new ApproverGroupInput(null, "Gerencia", false, true, new[] { Guid.NewGuid() }), CancellationToken.None);
        await env.Profiles.SaveAsync(new SupplierProfileInput(null, "77555010-K", SupplierConditions.FixedMonthly, group, null,
            new DateOnly(2026, 10, 1), null, null, true, overrides ?? Array.Empty<RuleOverrideInput>()), CancellationToken.None);
        return group;
    }

    [Fact]
    public async Task Run_PorEnviar_EnviaConClaveEstable_YQuedaAceptada()
    {
        var env = await new ReviewTestEnv().StartAsync(ModuleModes.Active);
        env.Sap.AddPurchaseOrder();
        var doc = env.Invoice();
        await env.EvaluateAsync();

        var result = await Due(env).RunAsync(CancellationToken.None);

        var review = await ReviewAsync(env, doc.Id);
        var request = Assert.Single(env.Inbox.Responses);
        Assert.Equal($"fp:{review.Id}:1", request.ResponseKey);
        Assert.Equal(DteResponseActions.AcceptContent, request.Action);
        Assert.Equal("rule:final", request.Actor);
        Assert.Equal(ReviewStatuses.Accepted, review.Status);
        Assert.Equal(1, result.Sent);
    }

    [Fact]
    public async Task Run_RespondidaEnOperador_NoEnvia_QuedaExterna()
    {
        var env = await new ReviewTestEnv().StartAsync(ModuleModes.Active);
        env.Sap.AddPurchaseOrder();
        var doc = env.Invoice();
        await env.EvaluateAsync();
        env.Inbox.NextRespondOutcome = DteRespondOutcome.AlreadyResponded;

        await Due(env).RunAsync(CancellationToken.None);
        await Due(env).RunAsync(CancellationToken.None);

        Assert.Equal(ReviewStatuses.External, (await ReviewAsync(env, doc.Id)).Status);
        Assert.Single(env.Inbox.Responses);
    }

    [Fact]
    public async Task Run_OperadorDiceNoRespondible_QuedaNoAdmiteRespuesta_YNoReingresa()
    {
        var env = await new ReviewTestEnv().StartAsync(ModuleModes.Active);
        env.Sap.AddPurchaseOrder();
        var doc = env.Invoice();
        await env.EvaluateAsync();
        env.Inbox.NextRespondOutcome = DteRespondOutcome.NotRespondable;

        await Due(env).RunAsync(CancellationToken.None);
        await env.EvaluateAsync(); // "procesar desde" sigue fijado: una fuera de alcance reingresaría al flujo
        await Due(env).RunAsync(CancellationToken.None);

        Assert.Equal(ReviewStatuses.NotRespondable, (await ReviewAsync(env, doc.Id)).Status);
        Assert.Single(env.Inbox.Responses);
    }

    [Fact]
    public async Task Run_FallaTresVeces_AvisaFinanzasUnaVez()
    {
        var env = await new ReviewTestEnv().StartAsync(ModuleModes.Active);
        env.Sap.AddPurchaseOrder();
        var doc = env.Invoice();
        await env.EvaluateAsync();

        for (var i = 0; i < 4; i++)
        {
            env.Inbox.NextRespondOutcome = DteRespondOutcome.Failed;
            await Due(env).RunAsync(CancellationToken.None);
        }

        var review = await ReviewAsync(env, doc.Id);
        Assert.Equal(ReviewStatuses.ToSend, review.Status);
        Assert.Equal(4, review.Attempts);
        Assert.NotNull(review.LastError);
        Assert.Single(env.Notifications.Enqueued, n => n.UserId == env.FinanceUser && n.Title.Contains("No se pudo responder"));
    }

    [Fact]
    public async Task Run_FallaElUltimoDia_AvisaFinanzas()
    {
        var env = await new ReviewTestEnv().StartAsync(ModuleModes.Active, new DateOnly(2026, 10, 1));
        env.Sap.AddPurchaseOrder(docDate: new DateOnly(2026, 10, 10));
        var doc = env.Invoice(day: 19); // vence hoy, 27-10
        await env.EvaluateAsync();
        env.Notifications.Enqueued.Clear();
        env.Inbox.NextRespondOutcome = DteRespondOutcome.Failed;

        await Due(env).RunAsync(CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 27), (await ReviewAsync(env, doc.Id)).LegalDeadlineOn);
        Assert.Single(env.Notifications.Enqueued, n => n.UserId == env.FinanceUser);
    }

    [Fact]
    public async Task Run_AvisoVencido_Rechaza()
    {
        var env = await new ReviewTestEnv().StartAsync(ModuleModes.Active);
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>()); // recibida 24-10: el aviso de 3 días vence hoy
        await env.EvaluateAsync();

        await Due(env).RunAsync(CancellationToken.None);

        var review = await ReviewAsync(env, doc.Id);
        Assert.Equal(ReviewStatuses.Rejected, review.Status);
        Assert.Equal("rule:oc-referenciada", review.DecisionSource);
        Assert.Equal(DteResponseActions.ClaimContent, Assert.Single(env.Inbox.Responses).Action);
    }

    [Fact]
    public async Task Run_UltimoDia_ReglaPlazoRechaza()
    {
        var env = await new ReviewTestEnv().StartAsync(processFrom: new DateOnly(2026, 10, 1));
        await FixedMonthlyAsync(env);
        var doc = env.Invoice(taxId: "77555010-K", day: 19, purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();

        await Due(env).RunAsync(CancellationToken.None);

        var review = await ReviewAsync(env, doc.Id);
        Assert.Equal(ReviewStatuses.Simulated, review.Status);
        Assert.Equal(RuleActions.Reject, review.DecidedAction);
        Assert.Equal("rule:plazo-por-vencer", review.DecisionSource);
        Assert.Empty(env.Inbox.Responses);
    }

    [Fact]
    public async Task Run_UltimoDia_OverrideProveedorAcepta()
    {
        var env = await new ReviewTestEnv().StartAsync(processFrom: new DateOnly(2026, 10, 1));
        await FixedMonthlyAsync(env, new[] { new RuleOverrideInput("plazo-por-vencer", RuleOverrideModes.Custom, RuleActions.Accept, null, null) });
        var doc = env.Invoice(taxId: "77555010-K", day: 19, purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();

        await Due(env).RunAsync(CancellationToken.None);

        Assert.Equal(RuleActions.Accept, (await ReviewAsync(env, doc.Id)).DecidedAction);
    }

    [Fact]
    public async Task Run_PasoAActivo_NoEnviaLasSimuladas()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder();
        var doc = env.Invoice();
        await env.EvaluateAsync();
        await env.Settings.SaveGeneralAsync(new GeneralSettingsInput { ProcessFromDate = new DateOnly(2026, 10, 20), Mode = ModuleModes.Active },
            CancellationToken.None);

        await env.EvaluateAsync();
        await Due(env).RunAsync(CancellationToken.None);

        Assert.Equal(ReviewStatuses.Simulated, (await ReviewAsync(env, doc.Id)).Status);
        Assert.Empty(env.Inbox.Responses);
    }

    [Fact]
    public async Task Run_PasadoElPlazo_Vencida()
    {
        var env = await new ReviewTestEnv().StartAsync(processFrom: new DateOnly(2026, 10, 1));
        await FixedMonthlyAsync(env);
        var doc = env.Invoice(taxId: "77555010-K", day: 18, purchaseOrders: Array.Empty<int>()); // venció el 26-10
        await env.EvaluateAsync();
        var review = await ReviewAsync(env, doc.Id);
        review.Status = ReviewStatuses.InReview;
        await env.Db.SaveChangesAsync();

        var result = await Due(env).RunAsync(CancellationToken.None);

        Assert.Equal(ReviewStatuses.Expired, (await ReviewAsync(env, doc.Id)).Status);
        Assert.Equal(1, result.Expired);
    }

    [Fact]
    public async Task Run_Escalamiento_AvisaFinanzasYGrupo_UnaVez()
    {
        var env = await new ReviewTestEnv().StartAsync(processFrom: new DateOnly(2026, 10, 1));
        await FixedMonthlyAsync(env);
        var doc = env.Invoice(taxId: "77555010-K", day: 21, purchaseOrders: Array.Empty<int>()); // vence 29-10: faltan 2 días
        await env.EvaluateAsync();
        env.Notifications.Enqueued.Clear();

        var first = await Due(env).RunAsync(CancellationToken.None);
        var second = await Due(env).RunAsync(CancellationToken.None);

        Assert.Equal(1, first.Escalated);
        Assert.Equal(0, second.Escalated);
        Assert.Contains(env.Notifications.Enqueued, n => n.UserId == env.FinanceUser);
        Assert.Equal(2, env.Notifications.Enqueued.Count);
        Assert.NotNull((await ReviewAsync(env, doc.Id)).EscalatedAt);
    }

    [Fact]
    public async Task Run_RechazoUsaTipoDeReclamoElegido()
    {
        var env = await new ReviewTestEnv().StartAsync(ModuleModes.Active);
        env.Sap.AddPurchaseOrder();
        var doc = env.Invoice();
        await env.EvaluateAsync();
        var review = await ReviewAsync(env, doc.Id);
        review.DecidedAction = RuleActions.Reject;
        review.ClaimType = DteResponseActions.ClaimPartialGoods;
        review.DecisionSource = "user";
        review.DecidedBy = env.FinanceUser;
        await env.Db.SaveChangesAsync();

        await Due(env).RunAsync(CancellationToken.None);

        var request = Assert.Single(env.Inbox.Responses);
        Assert.Equal(DteResponseActions.ClaimPartialGoods, request.Action);
        Assert.Equal($"user:{env.FinanceUser}", request.Actor);
        Assert.Equal(ReviewStatuses.Rejected, (await ReviewAsync(env, doc.Id)).Status);
    }

    private sealed class ThrowingReviews : IInvoiceReviewService
    {
        public Task<ReviewCycleResult> EvaluatePendingAsync(CancellationToken ct) => throw new InvalidOperationException("HANA caído");
        public Task EvaluateOneAsync(long reviewId, CancellationToken ct) => throw new NotSupportedException();
        public Task<InvoiceEvaluationContext?> BuildContextAsync(long reviewId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class CountingDue : IDueActionsService
    {
        public int Runs { get; private set; }
        public Task<DueRunResult> RunAsync(CancellationToken ct)
        {
            Runs++;
            return Task.FromResult(new DueRunResult(0, 0, 0, 0));
        }
    }

    /// <summary>C3 (revisión final): una falla de la evaluación no frena los envíos, vencimientos ni escalamientos.</summary>
    [Fact]
    public async Task Job_EvaluacionFalla_VencimientosYEnviosCorrenIgual()
    {
        var due = new CountingDue();
        var resets = 0;

        var (cycle, result) = await FacturasProveedorBackgroundService.RunCycleAsync(new ThrowingReviews(), due, () => resets++,
            NullLogger.Instance, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(cycle);
        Assert.NotNull(result);
        Assert.Equal(1, due.Runs);
        Assert.Equal(1, resets);
    }

    [Fact]
    public async Task Job_UnaCompaniaFalla_SigueConLasDemas()
    {
        var processed = new List<Guid>();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        await FacturasProveedorBackgroundService.RunForCompaniesAsync(ids, id =>
        {
            if (id == ids[1]) throw new InvalidOperationException("sin conexión");
            processed.Add(id);
            return Task.CompletedTask;
        }, NullLogger.Instance);

        Assert.Equal(new[] { ids[0], ids[2] }, processed);
    }
}
