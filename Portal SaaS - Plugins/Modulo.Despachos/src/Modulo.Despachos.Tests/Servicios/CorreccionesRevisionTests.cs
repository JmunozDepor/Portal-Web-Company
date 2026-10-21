using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Correcciones de la revisión final de la rama (manifiestos sin costo, recálculo, robustez).</summary>
public class CorreccionesRevisionTests
{
    private static async Task<(ManifestTestEnv Env, Guid Id, Guid DocId)> DispatchedAsync(
        ManifestTestEnv env, string? commune = "Rancagua")
    {
        env.AddSap(1, commune: commune);
        var id = await env.DraftWithDocsAsync(1);
        Assert.True((await env.Svc.DispatchAsync(id)).Success);
        var m = await env.Svc.GetAsync(id);
        return (env, id, m!.Documents.Single().Id);
    }

    private static Task ReturnAllAsync(ManifestTestEnv env, Guid id, Guid docId) =>
        env.Svc.RegisterReturnAsync(id,
            new[] { new DocumentReturnInput(docId, DeliveryStatuses.Delivered, null, null) }, null);

    // ------------------------------------------------------------ 1. Cerrar sin costo

    [Fact]
    public async Task Close_SinCosto_Lanza_HastaCorregirOAjustar()
    {
        var env = ManifestTestEnv.Build(seedRates: false);
        var (_, id, docId) = await DispatchedAsync(env);
        await ReturnAllAsync(env, id, docId);
        Assert.Equal(CostStatuses.NoRate, (await env.Svc.GetAsync(id))!.CostStatus);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.CloseAsync(id));
        Assert.Contains("no tiene costo", ex.Message);
        Assert.Equal(ManifestStatuses.Returned, (await env.Svc.GetAsync(id))!.Status);

        await env.Svc.OverrideCostAsync(id, 50000, "Acuerdo manual");
        await env.Svc.CloseAsync(id);
        Assert.Equal(ManifestStatuses.Closed, (await env.Svc.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task OverrideCost_EnCerrado_PeriodoAbierto_Permitido()
    {
        var env = ManifestTestEnv.Build();
        var (_, id, docId) = await DispatchedAsync(env);
        await ReturnAllAsync(env, id, docId);
        await env.Svc.CloseAsync(id);
        await env.Svc.OverrideCostAsync(id, 1234, "Ajuste posterior");
        var m = await env.Svc.GetAsync(id);
        Assert.Equal(1234m, m!.CostFinal);
        Assert.Equal(ManifestStatuses.Closed, m.Status);
    }

    // ------------------------------------------------------------ 2. Recalcular / corregir comuna

    [Fact]
    public async Task RecalculateCost_TrasCorregirTarifa_PasaDeSinTarifaAOk_YRegistraEvento()
    {
        var env = ManifestTestEnv.Build(seedRates: false);
        var (_, id, _) = await DispatchedAsync(env);
        Assert.Equal(CostStatuses.NoRate, (await env.Svc.GetAsync(id))!.CostStatus);

        await env.Tariff.SaveRateCardAsync(new RateCard { CommuneId = env.RancaguaId, Amount = 150000, ValidFrom = new DateOnly(2026, 1, 1) });
        var r = await env.Svc.RecalculateCostAsync(id);

        Assert.Equal(CostStatuses.Ok, r.Status);
        var m = await env.Svc.GetAsync(id);
        Assert.Equal(150000m, m!.CostFinal);
        var ev = Assert.Single(m.Events, e => e.EventType == ManifestEventTypes.CostRecalculated);
        Assert.Contains("no_rate", ev.PayloadJson);
        Assert.Contains("150000", ev.PayloadJson);
    }

    [Fact]
    public async Task RecalculateCost_BorradorAnuladoOPeriodoCerrado_Lanza()
    {
        var env = ManifestTestEnv.Build();
        var draft = await env.DraftWithDocsAsync(5);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.RecalculateCostAsync(draft));
        await env.Svc.AnnulAsync(draft, "x");
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.RecalculateCostAsync(draft));

        var (_, id, _) = await DispatchedAsync(env);
        env.Db.MonthlyClosures.Add(new MonthlyClosure
        { CompanyId = env.Company.CompanyId, Period = "2026-11", Status = ClosureStatuses.Closed, CreatedAt = DateTime.UtcNow });
        await env.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.RecalculateCostAsync(id));
        Assert.Contains("período cerrado", ex.Message);
    }

    [Fact]
    public async Task SetDocumentCommune_Despachado_CorrigeLaComunaYRecalcula()
    {
        var env = ManifestTestEnv.Build();
        var (_, id, docId) = await DispatchedAsync(env, commune: "Villa Nowhere");
        var before = await env.Svc.GetAsync(id);
        Assert.Null(before!.Documents.Single().DestinationCommuneId);
        Assert.Equal(CostStatuses.NoRate, before.CostStatus);

        await env.Svc.SetDocumentCommuneAsync(id, docId, env.CuricoId);

        var m = await env.Svc.GetAsync(id);
        Assert.Equal(env.CuricoId, m!.Documents.Single().DestinationCommuneId);
        Assert.Equal(CostStatuses.Ok, m.CostStatus);
        Assert.Equal(220000m, m.CostFinal);
        Assert.Contains(m.Events, e => e.EventType == ManifestEventTypes.DocumentCommuneChanged && e.ManifestDocumentId == docId);
    }

    [Fact]
    public async Task SetDocumentCommune_Borrador_NoRecalcula_YValidaComunaYEstado()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(7, commune: "Villa Nowhere");
        var id = await env.Svc.CreateDraftAsync(env.Header());
        await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 7));
        var docId = (await env.Svc.GetAsync(id))!.Documents.Single().Id;

        await env.Svc.SetDocumentCommuneAsync(id, docId, env.RancaguaId);
        var m = await env.Svc.GetAsync(id);
        Assert.Equal(env.RancaguaId, m!.Documents.Single().DestinationCommuneId);
        Assert.DoesNotContain(m.Events, e => e.EventType == ManifestEventTypes.CostRecalculated);

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.SetDocumentCommuneAsync(id, docId, 999999));
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.SetDocumentCommuneAsync(id, Guid.NewGuid(), env.RancaguaId));

        await env.Svc.AnnulAsync(id, "x");
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.SetDocumentCommuneAsync(id, docId, env.CuricoId));
    }

    // ------------------------------------------------------------ 6. Retornado en período cerrado

    [Fact]
    public async Task CierreMensual_PasaALosRetornadosAClosed_ConEvento()
    {
        var env = ManifestTestEnv.Build();
        var a = await MonthlyClosureServiceTests.SeedManifestAsync(env, 1, new DateOnly(2026, 9, 1), ManifestStatuses.Returned);
        var b = await MonthlyClosureServiceTests.SeedManifestAsync(env, 2, new DateOnly(2026, 9, 2), ManifestStatuses.Closed);

        Assert.True((await env.Closure.CloseAsync(2026, 9)).Closed);

        Assert.Equal(ManifestStatuses.Closed, (await env.Svc.GetAsync(a.Id))!.Status);
        Assert.Equal(ManifestStatuses.Closed, (await env.Svc.GetAsync(b.Id))!.Status);
        Assert.Contains(env.Db.ManifestEvents, e => e.ManifestId == a.Id && e.EventType == ManifestEventTypes.Closed
            && e.FromStatus == ManifestStatuses.Returned && e.ToStatus == ManifestStatuses.Closed
            && e.PayloadJson!.Contains("por cierre de período"));
        Assert.DoesNotContain(env.Db.ManifestEvents, e => e.ManifestId == b.Id && e.EventType == ManifestEventTypes.Closed);
    }

    // ------------------------------------------------------------ 7. PDF de borrador / anulado

    [Fact]
    public async Task Pdf_BorradorOAnulado_Rechaza_SinRegistrarEvento()
    {
        var env = ManifestTestEnv.Build();
        var pdf = new ManifestPdfService(env.Db, env.Company, env.User, env.Svc, env.Clock);
        var draft = await env.DraftWithDocsAsync(1);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pdf.RenderAsync(draft, false));
        Assert.Contains("despach", ex.Message);
        await env.Svc.AnnulAsync(draft, "x");
        await Assert.ThrowsAsync<InvalidOperationException>(() => pdf.RenderAsync(draft, false));
        Assert.DoesNotContain(env.Db.ManifestEvents, e => e.EventType == ManifestEventTypes.Printed);
    }

    // ------------------------------------------------------------ 10. Avisos robustos

    private sealed class FlakyNotifications : INotificationService
    {
        public List<Guid> Sent { get; } = new();
        public Guid FailFor;
        public Task<Guid> EnqueueAsync(Guid userId, string title, string message, string severity = "info", string? url = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Guid?> TryEnqueueAsync(Guid userId, string title, string message, string severity = "info", string? url = null, CancellationToken ct = default)
        {
            if (userId == FailFor) throw new InvalidOperationException("boom");
            Sent.Add(userId);
            return Task.FromResult<Guid?>(Guid.NewGuid());
        }
        public Task<IReadOnlyList<NotificationDto>> ListRecentAsync(Guid userId, int limit = 20, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    [Fact]
    public async Task Notify_UnDestinatarioFalla_LosDemasIgualRecibenElAviso()
    {
        var flaky = new FlakyNotifications();
        var env = ManifestTestEnv.Build(factory: (db, c, u, s, m, cost, t, n, cl) =>
            new ManifestService(db, c, u, s, m, cost, t, flaky, cl));
        var bad = Guid.NewGuid();
        var good = Guid.NewGuid();
        flaky.FailFor = bad;
        await env.Master.SaveNotificationRecipientAsync(new NotificationRecipient { UserId = bad });
        await env.Master.SaveNotificationRecipientAsync(new NotificationRecipient { UserId = good });
        var reason = await env.Master.SaveRejectionReasonAsync(new RejectionReason { Code = "R1", Name = "Dañado", AppliesTo = RejectionAppliesTo.Rejection });
        var (_, id, docId) = await DispatchedAsync(env);

        await env.Svc.RegisterReturnAsync(id,
            new[] { new DocumentReturnInput(docId, DeliveryStatuses.Rejected, reason, null) }, null);

        Assert.Equal(new[] { good }, flaky.Sent);
        Assert.Equal(ManifestStatuses.Returned, (await env.Svc.GetAsync(id))!.Status);
    }

    // ------------------------------------------------------------ 4. Borrados con dependientes vivos

    [Fact]
    public async Task DeleteRejectionReason_UsadoEnDocumento_Bloquea()
    {
        var env = ManifestTestEnv.Build();
        var reason = await env.Master.SaveRejectionReasonAsync(new RejectionReason { Code = "R1", Name = "Dañado", AppliesTo = RejectionAppliesTo.Rejection });
        var free = await env.Master.SaveRejectionReasonAsync(new RejectionReason { Code = "R2", Name = "Libre", AppliesTo = RejectionAppliesTo.Rejection });
        var (_, id, docId) = await DispatchedAsync(env);
        await env.Svc.RegisterReturnAsync(id,
            new[] { new DocumentReturnInput(docId, DeliveryStatuses.Rejected, reason, null) }, null);

        var r = await env.Master.DeleteRejectionReasonAsync(reason);
        Assert.False(r.Deleted);
        Assert.Contains("desactívelo", r.BlockedReason);
        Assert.True((await env.Master.DeleteRejectionReasonAsync(free)).Deleted);
    }

    [Fact]
    public async Task DeleteRateCard_UsadaEnManifiesto_Bloquea_YLibreSeElimina()
    {
        var env = ManifestTestEnv.Build();
        var (_, id, _) = await DispatchedAsync(env);
        var used = (await env.Svc.GetAsync(id))!.RateCardId!.Value;
        var free = await env.Tariff.SaveRateCardAsync(new RateCard { CommuneId = env.CuricoId, Amount = 1, ValidFrom = new DateOnly(2026, 1, 1), ValidTo = new DateOnly(2026, 1, 31) });

        var r = await env.Tariff.DeleteRateCardAsync(used);
        Assert.False(r.Deleted);
        Assert.Contains("manifiestos", r.BlockedReason);
        Assert.Contains("desactív", r.BlockedReason);
        Assert.True((await env.Tariff.DeleteRateCardAsync(free)).Deleted);
    }

    [Fact]
    public async Task DeleteRateCard_ReferenciadaSoloEnLineaDeCosto_Bloquea()
    {
        var env = ManifestTestEnv.Build();
        var (_, id, _) = await DispatchedAsync(env);
        var card = await env.Tariff.SaveRateCardAsync(new RateCard { CommuneId = env.CuricoId, Amount = 5, ValidFrom = new DateOnly(2026, 1, 1) });
        env.Db.ManifestCostLines.Add(new ManifestCostLine { CompanyId = env.Company.CompanyId, ManifestId = id, Kind = "base", RateCardId = card, Amount = 5, Explanation = "x" });
        await env.Db.SaveChangesAsync();
        Assert.False((await env.Tariff.DeleteRateCardAsync(card)).Deleted);
    }

    [Fact]
    public async Task DeleteSurcharge_UsadoEnLineaDeCosto_Bloquea_YLibreSeElimina()
    {
        var env = ManifestTestEnv.Build();
        var (_, id, _) = await DispatchedAsync(env);
        var used = await env.Tariff.SaveSurchargeAsync(new RateSurcharge
        { Name = "Nocturno", ConditionType = "is_holiday", ConditionParametersJson = "{}", EffectType = "fixed_amount", Value = 100, ValidFrom = new DateOnly(2026, 1, 1) });
        var free = await env.Tariff.SaveSurchargeAsync(new RateSurcharge
        { Name = "Otro", ConditionType = "is_holiday", ConditionParametersJson = "{}", EffectType = "fixed_amount", Value = 100, ValidFrom = new DateOnly(2026, 1, 1) });
        env.Db.ManifestCostLines.Add(new ManifestCostLine { CompanyId = env.Company.CompanyId, ManifestId = id, Kind = "surcharge", RateSurchargeId = used, Amount = 100, Explanation = "x" });
        await env.Db.SaveChangesAsync();

        var r = await env.Tariff.DeleteSurchargeAsync(used);
        Assert.False(r.Deleted);
        Assert.Contains("desactívelo", r.BlockedReason);
        Assert.True((await env.Tariff.DeleteSurchargeAsync(free)).Deleted);
    }

    // ------------------------------------------------------------ 16. Reporte mensual sin borradores

    [Fact]
    public async Task ReporteMensual_ExcluyeBorradores()
    {
        var env = ManifestTestEnv.Build();
        await MonthlyClosureServiceTests.SeedManifestAsync(env, 1, new DateOnly(2026, 9, 1), ManifestStatuses.Draft, 5000);
        await MonthlyClosureServiceTests.SeedManifestAsync(env, 2, new DateOnly(2026, 9, 2), ManifestStatuses.Dispatched, 7000);
        await MonthlyClosureServiceTests.SeedManifestAsync(env, 3, new DateOnly(2026, 9, 3), ManifestStatuses.Closed, 11000);
        var report = await new DispatchReportService(env.Db, env.Company).GetMonthlyAsync(2026, 9);
        Assert.Equal(18000m, report.TotalCost);
        Assert.Equal(2, report.Days.Count);
    }

    // ------------------------------------------------------------ 11. Vehículo vs carro

    [Fact]
    public async Task Header_RechazaCarroComoVehiculo_YVehiculoComoCarro()
    {
        var env = ManifestTestEnv.Build();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => env.Svc.CreateDraftAsync(env.Header(vehicleId: env.TrailerId)));
        Assert.Contains("vehículo", ex.Message);

        var h = env.Header() with { TrailerId = env.VehicleId };
        ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.CreateDraftAsync(h));
        Assert.Contains("carro", ex.Message);

        var id = await env.Svc.CreateDraftAsync(env.Header());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => env.Svc.UpdateDraftHeaderAsync(id, env.Header(vehicleId: env.TrailerId)));
    }

    [Fact]
    public async Task ChangeVehicle_RechazaCarroComoVehiculo_YVehiculoComoCarro()
    {
        var env = ManifestTestEnv.Build();
        var (_, id, _) = await DispatchedAsync(env);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => env.Svc.ChangeVehicleOrDriverAsync(id, env.TrailerId, null, env.DriverId, "x"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => env.Svc.ChangeVehicleOrDriverAsync(id, env.VehicleId, env.VehicleId, env.DriverId, "x"));
    }
}
