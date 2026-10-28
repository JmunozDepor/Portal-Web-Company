using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Models;
using Modulo.FacturasProveedor.Servicios;
using Modulo.FacturasProveedor.Tests.Fakes;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.FacturasProveedor.Tests.Servicios;

public class InvoiceReviewServiceTests
{
    private static Task<FpInvoiceReview> ReviewAsync(ReviewTestEnv env, long receivedId) =>
        env.Db.InvoiceReviews.SingleAsync(r => r.ReceivedId == receivedId);

    [Fact]
    public async Task Evaluate_RecibidaAntesDeProcesarDesde_FueraDeAlcance()
    {
        var env = await new ReviewTestEnv().StartAsync(processFrom: new DateOnly(2026, 10, 25));
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>());

        await env.EvaluateAsync();

        var review = await ReviewAsync(env, doc.Id);
        Assert.Equal(ReviewStatuses.OutOfScope, review.Status);
        Assert.Null(review.DecidedAction);
        Assert.Empty(env.Inbox.Responses);
    }

    [Fact]
    public async Task Evaluate_SinProcesarDesde_TodoFueraDeAlcance()
    {
        var env = new ReviewTestEnv();
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>());

        await env.EvaluateAsync();

        Assert.Equal(ReviewStatuses.OutOfScope, (await ReviewAsync(env, doc.Id)).Status);
    }

    [Fact]
    public async Task Evaluate_ProcesarDesdeSeFijaDespues_EntranLasDesdeEsaFecha()
    {
        var env = new ReviewTestEnv();
        env.Sap.AddPurchaseOrder();
        var doc = env.Invoice();
        await env.EvaluateAsync();

        await env.StartAsync(processFrom: new DateOnly(2026, 10, 24));
        await env.EvaluateAsync();

        Assert.Equal(ReviewStatuses.Simulated, (await ReviewAsync(env, doc.Id)).Status);
    }

    [Fact]
    public async Task Evaluate_CalzaConOc_Simulacion_QuedaSimulada_SinResponder()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder();
        var doc = env.Invoice(total: 2_200_000);

        var result = await env.EvaluateAsync();

        var review = await ReviewAsync(env, doc.Id);
        Assert.Equal(1, result.Created);
        Assert.Equal(ReviewStatuses.Simulated, review.Status);
        Assert.True(review.IsSimulated);
        Assert.Equal(RuleActions.Accept, review.DecidedAction);
        Assert.Equal("rule:final", review.DecisionSource);
        Assert.Equal(new DateOnly(2026, 10, 24), review.SiiReceivedOn);
        Assert.Equal(new DateOnly(2026, 11, 1), review.LegalDeadlineOn);
        Assert.Empty(env.Inbox.Responses);
        Assert.NotEmpty(await env.Db.ReviewRuleResults.Where(r => r.ReviewId == review.Id).ToListAsync());
    }

    [Fact]
    public async Task Evaluate_CalzaConOc_Activo_QuedaPorEnviar()
    {
        var env = await new ReviewTestEnv().StartAsync(ModuleModes.Active);
        env.Sap.AddPurchaseOrder();
        var doc = env.Invoice();

        await env.EvaluateAsync();

        var review = await ReviewAsync(env, doc.Id);
        Assert.Equal(ReviewStatuses.ToSend, review.Status);
        Assert.False(review.IsSimulated);
        Assert.Equal(1, review.DecisionNumber);
    }

    [Fact]
    public async Task Evaluate_SinOc_QuedaEnAvisoConFecha_YAvisaFinanzas()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>());

        await env.EvaluateAsync();
        await env.EvaluateAsync();

        var review = await ReviewAsync(env, doc.Id);
        Assert.Equal(ReviewStatuses.Warning, review.Status);
        Assert.Equal(new DateOnly(2026, 10, 27), review.WarnDeadlineOn);
        var notice = Assert.Single(env.Notifications.Enqueued);
        Assert.Equal(env.FinanceUser, notice.UserId);
        Assert.Contains("sin OC", notice.Message);
        Assert.Equal($"/facturas-proveedor/bandeja/{review.Id}", notice.Url);
    }

    [Fact]
    public async Task Evaluate_GastoFijo_VaARevision_YAvisaAlGrupo()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var approver = Guid.NewGuid();
        var group = await env.Groups.SaveAsync(new ApproverGroupInput(null, "Gerencia", false, true, new[] { approver }), CancellationToken.None);
        await env.Profiles.SaveAsync(new SupplierProfileInput(null, "77555010-K", SupplierConditions.FixedMonthly, group, null,
            new DateOnly(2026, 10, 1), null, null, true, Array.Empty<RuleOverrideInput>()), CancellationToken.None);
        var doc = env.Invoice(taxId: "77555010-K", purchaseOrders: Array.Empty<int>(), total: 4_100_000);

        await env.EvaluateAsync();
        await env.EvaluateAsync();

        var review = await ReviewAsync(env, doc.Id);
        Assert.Equal(ReviewStatuses.InReview, review.Status);
        Assert.Equal(group, review.AssignedGroupId);
        Assert.Equal(approver, Assert.Single(env.Notifications.Enqueued).UserId);
    }

    [Fact]
    public async Task Evaluate_DosParcialesMismaOc_LaSegundaSuperaSaldo_SeRechaza()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder(total: 3_200_000);
        var first = env.Invoice(folio: 1, total: 2_000_000, hourUtc: 12);
        var second = env.Invoice(folio: 2, total: 1_500_000, hourUtc: 13);

        await env.EvaluateAsync();

        Assert.Equal(RuleActions.Accept, (await ReviewAsync(env, first.Id)).DecidedAction);
        var rejected = await ReviewAsync(env, second.Id);
        Assert.Equal(RuleActions.Reject, rejected.DecidedAction);
        Assert.Equal("rule:oc-saldo", rejected.DecisionSource);
    }

    [Fact]
    public async Task Evaluate_AceptadaEnFaceleCuentaParaElSaldo()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder(total: 3_200_000);
        env.Invoice(folio: 1, total: 2_000_000, day: 21, responseStatus: DteResponseStatuses.External, responseAction: DteResponseActions.AcceptContent);
        var doc = env.Invoice(folio: 2, total: 1_500_000);

        await env.EvaluateAsync();

        Assert.Equal(RuleActions.Reject, (await ReviewAsync(env, doc.Id)).DecidedAction);
    }

    [Fact]
    public async Task Evaluate_ExternaEnInbox_QuedaExterna()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var doc = env.Invoice(responseStatus: DteResponseStatuses.External, responseAction: DteResponseActions.AcceptContent);

        await env.EvaluateAsync();

        Assert.Equal(ReviewStatuses.External, (await ReviewAsync(env, doc.Id)).Status);
    }

    [Theory]
    [InlineData(ModuleModes.Active, 1)]
    [InlineData(ModuleModes.Simulation, 0)]
    public async Task Evaluate_ExternaEnActivo_AvisaFinanzas_EnSimulacionNo(string mode, int expectedNotices)
    {
        var env = await new ReviewTestEnv().StartAsync(mode);
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();
        env.Notifications.Enqueued.Clear();

        env.Inbox.Replace(doc with
        {
            ResponseStatus = DteResponseStatuses.External, ResponseAction = DteResponseActions.ClaimContent, UpdatedAt = doc.UpdatedAt.AddHours(1),
        });
        await env.EvaluateAsync();

        Assert.Equal(ReviewStatuses.External, (await ReviewAsync(env, doc.Id)).Status);
        Assert.Equal(expectedNotices, env.Notifications.Enqueued.Count(n => n.Title.Contains("fuera del portal")));
    }

    [Fact]
    public async Task Evaluate_OcAsociadaDespues_SaleDeAviso()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder();
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();
        Assert.Equal(ReviewStatuses.Warning, (await ReviewAsync(env, doc.Id)).Status);

        await env.Inbox.SetPurchaseOrderRefsAsync(doc.Id, new[] { 4512 }, env.User.UserId);
        await env.EvaluateAsync();

        var review = await ReviewAsync(env, doc.Id);
        Assert.Equal(ReviewStatuses.Simulated, review.Status);
        Assert.Equal(RuleActions.Accept, review.DecidedAction);
    }

    [Fact]
    public async Task Evaluate_TipoNoRespondible_NoCreaRevision()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Invoice(dteType: 61);

        await env.EvaluateAsync();

        Assert.Empty(await env.Db.InvoiceReviews.ToListAsync());
    }

    private const string Contado = "Pagada al contado: el SII no admite aceptarla ni reclamarla.";

    [Fact]
    public async Task Evaluate_PagadaAlContado_NoAdmiteRespuesta_SinEvaluarNiAvisar()
    {
        var env = await new ReviewTestEnv().StartAsync(ModuleModes.Active);
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>(), notRespondableReason: Contado);

        await env.EvaluateAsync();

        var review = await ReviewAsync(env, doc.Id);
        Assert.Equal(ReviewStatuses.NotRespondable, review.Status);
        Assert.Null(review.DecidedAction);
        Assert.Empty(await env.Db.ReviewRuleResults.Where(r => r.ReviewId == review.Id).ToListAsync());
        Assert.Empty(env.Inbox.Responses);
        Assert.Empty(env.Notifications.Enqueued);
        var ev = Assert.Single(await env.Db.ReviewEvents.Where(e => e.ReviewId == review.Id).ToListAsync());
        Assert.Equal("not_respondable", ev.EventType);
        Assert.Equal(Contado, ev.Detail);
    }

    [Fact]
    public async Task Evaluate_EnAviso_SiPasaANoRespondible_SaleDelFlujo()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();
        Assert.Equal(ReviewStatuses.Warning, (await ReviewAsync(env, doc.Id)).Status);

        env.Inbox.Replace(doc with { NotRespondableReason = Contado, UpdatedAt = doc.UpdatedAt.AddHours(1) });
        await env.EvaluateAsync();

        Assert.Equal(ReviewStatuses.NotRespondable, (await ReviewAsync(env, doc.Id)).Status);
    }

    [Fact]
    public async Task Evaluate_PagadaAlContadoCuentaParaElSaldo()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder(total: 3_200_000);
        env.Invoice(folio: 1, total: 2_000_000, hourUtc: 12, notRespondableReason: Contado);
        var doc = env.Invoice(folio: 2, total: 1_500_000, hourUtc: 13);

        await env.EvaluateAsync();

        var review = await ReviewAsync(env, doc.Id);
        Assert.Equal(RuleActions.Reject, review.DecidedAction);
        Assert.Equal("rule:oc-saldo", review.DecisionSource);
    }

    /// <summary>
    /// C1 (revisión final): la sincronización fija UpdatedAt al empezar y confirma al final; si entretanto otro escritor
    /// movió el cursor, la fila llega con UpdatedAt anterior al cursor. Igual debe tener revisión.
    /// </summary>
    [Fact]
    public async Task Evaluate_FilaConfirmadaConUpdatedAtAnteriorAlCursor_IgualTieneRevision()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder();
        var first = env.Invoice(folio: 1, total: 1_000_000);
        await env.EvaluateAsync();

        var late = env.Inbox.Add(folio: 2, total: 1_000_000, siiReceivedAt: ReviewTestEnv.ReceivedAt(24, 13), purchaseOrders: new[] { 4512 },
            updatedAt: first.UpdatedAt.AddMinutes(-10));
        await env.EvaluateAsync();

        var review = await ReviewAsync(env, late.Id);
        Assert.Equal(ReviewStatuses.Simulated, review.Status);
    }

    /// <summary>C2 (revisión final): notas de crédito y guías que referencian la OC no consumen su saldo.</summary>
    [Fact]
    public async Task Evaluate_NotaDeCreditoYGuiaConLaMismaOc_NoConsumenSaldo()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder(total: 2_200_000);
        env.Invoice(folio: 7, total: 2_200_000, dteType: 61, hourUtc: 10, notRespondableReason: "El SII no admite aceptar ni reclamar este documento.");
        env.Invoice(folio: 8, total: 2_200_000, dteType: 52, day: 10);
        var doc = env.Invoice(folio: 9, total: 2_200_000, hourUtc: 12);

        await env.EvaluateAsync();

        Assert.Equal(RuleActions.Accept, (await ReviewAsync(env, doc.Id)).DecidedAction);
    }

    /// <summary>C3 (revisión final): si SAP falla para una factura, las demás se evalúan igual y la que falló queda con el error.</summary>
    [Fact]
    public async Task Evaluate_SapFallaEnUnaFactura_LasDemasSeEvaluan_YQuedaElError()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder();
        env.Sap.FailingTaxIds.Add("77555010-K");
        var failing = env.Invoice(folio: 1, taxId: "77555010-K", purchaseOrders: Array.Empty<int>(), hourUtc: 11);
        var ok = env.Invoice(folio: 2, hourUtc: 12);

        await env.EvaluateAsync();

        Assert.Equal(ReviewStatuses.Simulated, (await ReviewAsync(env, ok.Id)).Status);
        var stuck = await ReviewAsync(env, failing.Id);
        Assert.Equal(ReviewStatuses.New, stuck.Status);
        Assert.Contains("SAP no responde", stuck.LastError);

        env.Sap.FailingTaxIds.Clear();
        await env.EvaluateAsync();
        Assert.Null((await ReviewAsync(env, failing.Id)).LastError);
    }

    /// <summary>I1 (revisión final): reevaluar sin cambios no invalida el sello con que el aprobador abrió la factura.</summary>
    [Fact]
    public async Task Evaluate_SinCambios_NoCambiaElSelloDeConcurrencia()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();
        var stamp = (await ReviewAsync(env, doc.Id)).ConcurrencyStamp;

        env.Clock.Now = env.Clock.Now.AddMinutes(5);
        await env.EvaluateAsync();

        var review = await ReviewAsync(env, doc.Id);
        Assert.Equal(ReviewStatuses.Warning, review.Status);
        Assert.Equal(stamp, review.ConcurrencyStamp);
    }

    /// <summary>I4 (revisión final): en modo activo, mover "procesar desde" hacia atrás no reabre facturas fuera de alcance.</summary>
    [Fact]
    public async Task Evaluate_EnActivo_MoverProcesarDesdeAtras_NoReabreFueraDeAlcance()
    {
        var env = await new ReviewTestEnv().StartAsync(ModuleModes.Active, processFrom: new DateOnly(2026, 10, 25));
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();
        Assert.Equal(ReviewStatuses.OutOfScope, (await ReviewAsync(env, doc.Id)).Status);

        await env.Settings.SaveGeneralAsync(new GeneralSettingsInput { ProcessFromDate = new DateOnly(2026, 10, 20), Mode = ModuleModes.Active },
            CancellationToken.None);
        await env.EvaluateAsync();

        Assert.Equal(ReviewStatuses.OutOfScope, (await ReviewAsync(env, doc.Id)).Status);
        Assert.Empty(env.Inbox.Responses);
    }

    /// <summary>I7 (revisión final): un ciclo web (ej. "Asociar OC") en paralelo con el del job no crea dos revisiones.</summary>
    [Fact]
    public async Task Evaluate_DosCiclosALaVez_UnaSolaRevisionPorFactura()
    {
        var name = "fp-" + Guid.NewGuid();
        var env = await new ReviewTestEnv(name).StartAsync();
        env.Sap.AddPurchaseOrder();
        env.Invoice();
        env.Inbox.ListDelay = TimeSpan.FromMilliseconds(150);
        var db2 = TestDb.Create(name);
        var settings2 = new ModuleSettingsService(db2, env.Engine, env.Company, env.User, env.Clock);
        var profiles2 = new SupplierProfileService(db2, env.Sap, env.Engine, env.Company, env.User, env.Clock);
        var reviews2 = new InvoiceReviewService(db2, env.Inbox, env.Engine, settings2, env.Sap, profiles2,
            new ApproverGroupService(db2, env.Company, env.User, env.Clock), env.Notifications, env.Company, env.Clock);

        await Task.WhenAll(env.EvaluateAsync(), reviews2.EvaluatePendingAsync(CancellationToken.None));

        Assert.Equal(1, await TestDb.Create(name).InvoiceReviews.CountAsync());
    }

    [Fact]
    public async Task Evaluate_Cursor_NoReprocesaLoYaLeido()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder();
        env.Invoice();
        await env.EvaluateAsync();

        var second = await env.EvaluateAsync();

        Assert.Equal(0, second.Created);
        Assert.NotNull((await env.Db.Settings.SingleAsync()).InboxCursor);
    }
}
