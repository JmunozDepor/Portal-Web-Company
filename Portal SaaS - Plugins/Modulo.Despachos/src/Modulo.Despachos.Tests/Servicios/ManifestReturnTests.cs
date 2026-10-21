using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Tests.Servicios;

public class ManifestReturnTests
{
    private static async Task<(ManifestTestEnv Env, Guid Id, List<Guid> DocIds)> DispatchedAsync(params long[] folios)
    {
        var env = ManifestTestEnv.Build();
        return await DispatchedAsync(env, folios);
    }

    private static async Task<(ManifestTestEnv Env, Guid Id, List<Guid> DocIds)> DispatchedAsync(ManifestTestEnv env, params long[] folios)
    {
        var id = await env.DraftWithDocsAsync(folios);
        var r = await env.Svc.DispatchAsync(id);
        Assert.True(r.Success);
        var m = await env.Svc.GetAsync(id);
        return (env, id, m!.Documents.OrderBy(d => d.Folio).Select(d => d.Id).ToList());
    }

    private static async Task<long> ReasonAsync(ManifestTestEnv env, string appliesTo = RejectionAppliesTo.Rejection, bool active = true) =>
        await env.Master.SaveRejectionReasonAsync(new RejectionReason
        { Code = "R" + Guid.NewGuid().ToString("N")[..6], Name = "Producto dañado", AppliesTo = appliesTo, IsActive = active });

    [Theory]
    [InlineData(DeliveryStatuses.PartiallyDelivered)]
    [InlineData(DeliveryStatuses.Rejected)]
    [InlineData(DeliveryStatuses.NotDelivered)]
    public async Task Return_ParcialRechazadoNoEntregado_ExigenMotivo(string status)
    {
        var (env, id, docs) = await DispatchedAsync(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], status, null, null) }, null));
        var m = await env.Svc.GetAsync(id);
        Assert.Equal(DeliveryStatuses.Dispatched, m!.Documents[0].DeliveryStatus);
    }

    [Fact]
    public async Task Return_MotivoDeOtroTipoOInactivo_Falla()
    {
        var (env, id, docs) = await DispatchedAsync(1);
        var notDeliveredReason = await ReasonAsync(env, RejectionAppliesTo.NotDelivered);
        var inactive = await ReasonAsync(env, active: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Rejected, notDeliveredReason, null) }, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Rejected, inactive, null) }, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.NotDelivered, inactive, null) }, null));
    }

    [Fact]
    public async Task Return_EstadoInvalidoODocumentoAjeno_Falla()
    {
        var (env, id, docs) = await DispatchedAsync(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Pending, null, null) }, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(Guid.NewGuid(), DeliveryStatuses.Delivered, null, null) }, null));
    }

    [Fact]
    public async Task Return_SoloEnDespachadoORetornado()
    {
        var env = ManifestTestEnv.Build();
        var draft = await env.DraftWithDocsAsync(1);
        var d = (await env.Svc.GetAsync(draft))!.Documents[0].Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.RegisterReturnAsync(draft, new[] { new DocumentReturnInput(d, DeliveryStatuses.Delivered, null, null) }, null));
    }

    [Fact]
    public async Task Return_TodosConEstado_ManifiestoPasaAReturned_ParcialSigueDespachado()
    {
        var (env, id, docs) = await DispatchedAsync(1, 2);
        await env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Delivered, null, null) }, null);
        var m = await env.Svc.GetAsync(id);
        Assert.Equal(ManifestStatuses.Dispatched, m!.Status);
        Assert.Null(m.ReturnedAt);
        Assert.Equal(DeliveryStatuses.Delivered, m.Documents.First(d => d.Id == docs[0]).DeliveryStatus);
        Assert.Equal(env.User.UserId, m.Documents.First(d => d.Id == docs[0]).ReturnedBy);

        await env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[1], DeliveryStatuses.Delivered, null, "ok") }, null);
        m = await env.Svc.GetAsync(id);
        Assert.Equal(ManifestStatuses.Returned, m!.Status);
        Assert.NotNull(m.ReturnedAt);
        Assert.Equal(DateTimeKind.Utc, m.ReturnedAt!.Value.Kind);
        Assert.Contains(m.Events, e => e.EventType == ManifestEventTypes.ReturnRegistered && e.ManifestDocumentId == docs[1]
            && e.FromStatus == DeliveryStatuses.Dispatched && e.ToStatus == DeliveryStatuses.Delivered && e.UserId == env.User.UserId);
        Assert.Contains(m.Events, e => e.EventType == ManifestEventTypes.ReturnRegistered && e.ManifestDocumentId == null
            && e.FromStatus == ManifestStatuses.Dispatched && e.ToStatus == ManifestStatuses.Returned);
    }

    [Fact]
    public async Task Return_Retornado_PermiteCorregirAntesDeCerrar_YNotificaSoloSiEntraARechazo()
    {
        var (env, id, docs) = await DispatchedAsync(1);
        await env.Master.SaveNotificationRecipientAsync(new NotificationRecipient { UserId = Guid.NewGuid() });
        await env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Delivered, null, null) }, null);
        Assert.Equal(ManifestStatuses.Returned, (await env.Svc.GetAsync(id))!.Status);
        Assert.Empty(env.Notifications.Enqueued);

        var reason = await ReasonAsync(env);
        await env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Rejected, reason, "x") }, null);
        Assert.Single(env.Notifications.Enqueued);
        // Repetir el mismo estado no vuelve a avisar.
        await env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Rejected, reason, "y") }, null);
        Assert.Single(env.Notifications.Enqueued);
        Assert.Equal(ManifestStatuses.Returned, (await env.Svc.GetAsync(id))!.Status);

        await env.Svc.CloseAsync(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Delivered, null, null) }, null));
    }

    [Fact]
    public async Task Return_RechazoNotificaACadaDestinatario()
    {
        var (env, id, docs) = await DispatchedAsync(1500);
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var inactive = Guid.NewGuid();
        await env.Master.SaveNotificationRecipientAsync(new NotificationRecipient { UserId = u1 });
        await env.Master.SaveNotificationRecipientAsync(new NotificationRecipient { UserId = u2 });
        await env.Master.SaveNotificationRecipientAsync(new NotificationRecipient { UserId = inactive, IsActive = false });
        var reason = await ReasonAsync(env);

        await env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Rejected, reason, "Caja rota") }, null);

        var m = await env.Svc.GetAsync(id);
        Assert.Equal(2, env.Notifications.Enqueued.Count);
        Assert.Equal(new[] { u1, u2 }.OrderBy(x => x), env.Notifications.Enqueued.Select(x => x.UserId).OrderBy(x => x));
        foreach (var n in env.Notifications.Enqueued)
        {
            Assert.Contains("Rechazo", n.Title);
            Assert.Contains(m!.Number.ToString(), n.Message);
            Assert.Contains("1500", n.Message);
            Assert.Contains("Cliente SA", n.Message);
            Assert.Contains("Producto dañado", n.Message);
            Assert.Contains("Caja rota", n.Message);
            Assert.Equal(NotificationSeverity.Warning, n.Severity);
            Assert.Equal($"/despachos/manifiestos/{id:D}", n.Url);
        }
    }

    [Fact]
    public async Task Return_Parcial_NotificaEntregaParcial_YEntregadoNoNotifica()
    {
        var (env, id, docs) = await DispatchedAsync(1, 2);
        await env.Master.SaveNotificationRecipientAsync(new NotificationRecipient { UserId = Guid.NewGuid() });
        var reason = await ReasonAsync(env);
        await env.Svc.RegisterReturnAsync(id, new[]
        {
            new DocumentReturnInput(docs[0], DeliveryStatuses.PartiallyDelivered, reason, null),
            new DocumentReturnInput(docs[1], DeliveryStatuses.Delivered, null, null),
        }, null);
        var n = Assert.Single(env.Notifications.Enqueued);
        Assert.Contains("Entrega parcial", n.Title);
    }

    [Fact]
    public async Task Return_NoEntregado_LiberaDocumento()
    {
        var (env, id, docs) = await DispatchedAsync(1);
        var reason = await ReasonAsync(env, RejectionAppliesTo.NotDelivered);
        await env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.NotDelivered, reason, "Cerrado") }, null);
        Assert.Empty(env.Notifications.Enqueued);

        var second = await env.Svc.CreateDraftAsync(env.Header());
        var r = await env.Svc.AddDocumentAsync(second, new DocumentCapture(null, DocumentKinds.Invoice, 1));
        Assert.Equal(AddDocumentOutcomes.Added, r.Outcome);

        var first = await env.Svc.GetAsync(id);
        Assert.Equal(DeliveryStatuses.NotDelivered, first!.Documents[0].DeliveryStatus);
        Assert.Equal(reason, first.Documents[0].RejectionReasonId);
        Assert.Single(first.Documents);
    }

    [Fact]
    public async Task Return_ConOdometro_RecalculaCostoPerKm()
    {
        var env = ManifestTestEnv.Build(seedRates: false);
        await env.Tariff.SaveRateCardAsync(new RateCard
        { CommuneId = env.RancaguaId, Amount = 900, CalculationMethod = "per_km", ValidFrom = new DateOnly(2026, 1, 1) });
        var (_, id, docs) = await DispatchedAsync(env, 1);
        Assert.Equal(0m, (await env.Svc.GetAsync(id))!.CostCalculated); // sin km aún

        await env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Delivered, null, null) }, 1180);
        var m = await env.Svc.GetAsync(id);
        Assert.Equal(1180, m!.OdometerEnd);
        Assert.Equal(162000m, m.CostCalculated);
        Assert.Equal(162000m, m.CostFinal);
    }

    [Fact]
    public async Task Return_Odometro_MenorAlInicioOSinInicio_Falla()
    {
        var (env, id, docs) = await DispatchedAsync(1); // inicio 1000
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Delivered, null, null) }, 900));
        Assert.Equal(DeliveryStatuses.Dispatched, (await env.Svc.GetAsync(id))!.Documents[0].DeliveryStatus);

        var env2 = ManifestTestEnv.Build();
        var draft = await env2.Svc.CreateDraftAsync(env2.Header() with { OdometerStart = null });
        env2.AddSap(7);
        await env2.Svc.AddDocumentAsync(draft, new DocumentCapture(null, DocumentKinds.Invoice, 7));
        await env2.Svc.DispatchAsync(draft);
        var d = (await env2.Svc.GetAsync(draft))!.Documents[0].Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env2.Svc.RegisterReturnAsync(draft, new[] { new DocumentReturnInput(d, DeliveryStatuses.Delivered, null, null) }, 1500));
    }

    [Fact]
    public async Task Annul_DesdeBorradorODespachado_LiberaDocumentos_YNoBorra()
    {
        var (env, id, _) = await DispatchedAsync(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.AnnulAsync(id, "  "));
        await env.Svc.AnnulAsync(id, "Error de armado");
        var m = await env.Svc.GetAsync(id);
        Assert.Equal(ManifestStatuses.Annulled, m!.Status);
        Assert.Single(m.Documents);
        Assert.Contains(m.Events, e => e.EventType == ManifestEventTypes.Annulled && e.ToStatus == ManifestStatuses.Annulled
            && e.FromStatus == ManifestStatuses.Dispatched && e.PayloadJson!.Contains("Error de armado"));

        var other = await env.Svc.CreateDraftAsync(env.Header());
        Assert.Equal(AddDocumentOutcomes.Added,
            (await env.Svc.AddDocumentAsync(other, new DocumentCapture(null, DocumentKinds.Invoice, 1))).Outcome);

        var draft = await env.Svc.CreateDraftAsync(env.Header());
        await env.Svc.AnnulAsync(draft, "no va");
        Assert.Equal(ManifestStatuses.Annulled, (await env.Svc.GetAsync(draft))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.AnnulAsync(draft, "otra vez"));
    }

    [Fact]
    public async Task Annul_NoPermitidoDesdeReturnedNiClosed()
    {
        var (env, id, docs) = await DispatchedAsync(1);
        await env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Delivered, null, null) }, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.AnnulAsync(id, "x"));
        await env.Svc.CloseAsync(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.AnnulAsync(id, "x"));
    }

    [Fact]
    public async Task OverrideCost_ExigeMotivo_YCostFinalEsOverride()
    {
        var (env, id, docs) = await DispatchedAsync(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.OverrideCostAsync(id, 100000, " "));
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.OverrideCostAsync(id, -1, "motivo"));

        await env.Svc.OverrideCostAsync(id, 99000, "Acuerdo con el transportista");
        var m = await env.Svc.GetAsync(id);
        Assert.Equal(150000m, m!.CostCalculated);
        Assert.Equal(99000m, m.CostOverride);
        Assert.Equal(99000m, m.CostFinal);
        Assert.Equal("Acuerdo con el transportista", m.CostOverrideReason);
        var ev = Assert.Single(m.Events, e => e.EventType == ManifestEventTypes.CostOverridden);
        Assert.Contains("150000", ev.PayloadJson);
        Assert.Contains("99000", ev.PayloadJson);

        // Un recálculo (retorno con odómetro) no pisa el override.
        await env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Delivered, null, null) }, 1100);
        Assert.Equal(99000m, (await env.Svc.GetAsync(id))!.CostFinal);

        // Permitido también en returned y, con el período abierto, en cerrado.
        await env.Svc.OverrideCostAsync(id, 0, "sin cobro");
        Assert.Equal(0m, (await env.Svc.GetAsync(id))!.CostFinal);
        await env.Svc.CloseAsync(id);
        await env.Svc.OverrideCostAsync(id, 5, "x");
        Assert.Equal(5m, (await env.Svc.GetAsync(id))!.CostFinal);
    }

    [Fact]
    public async Task OverrideCost_NoPermitidoEnBorradorNiAnulado()
    {
        var env = ManifestTestEnv.Build();
        var draft = await env.DraftWithDocsAsync(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.OverrideCostAsync(draft, 1, "x"));
        await env.Svc.AnnulAsync(draft, "x");
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.OverrideCostAsync(draft, 1, "x"));
    }

    [Fact]
    public async Task Close_SoloDesdeReturned()
    {
        var (env, id, docs) = await DispatchedAsync(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.CloseAsync(id));
        await env.Svc.RegisterReturnAsync(id, new[] { new DocumentReturnInput(docs[0], DeliveryStatuses.Delivered, null, null) }, null);
        await env.Svc.CloseAsync(id);
        var m = await env.Svc.GetAsync(id);
        Assert.Equal(ManifestStatuses.Closed, m!.Status);
        Assert.Contains(m.Events, e => e.EventType == ManifestEventTypes.Closed && e.FromStatus == ManifestStatuses.Returned
            && e.ToStatus == ManifestStatuses.Closed && e.UserId == env.User.UserId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.CloseAsync(id));
    }
}
