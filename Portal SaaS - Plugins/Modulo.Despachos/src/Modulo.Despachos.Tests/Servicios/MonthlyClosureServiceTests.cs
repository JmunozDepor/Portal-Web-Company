using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

public class MonthlyClosureServiceTests
{
    internal static async Task<Manifest> SeedManifestAsync(
        ManifestTestEnv env, int number, DateOnly date, string status = ManifestStatuses.Returned,
        decimal? costFinal = 100000, string costStatus = CostStatuses.Ok, long? carrierId = null,
        decimal units = 10, int docs = 2, int packages = 5)
    {
        var m = new Manifest
        {
            Id = Guid.NewGuid(), CompanyId = env.Company.CompanyId, Number = number, Status = status,
            DispatchDate = date, ShiftId = env.ShiftId, OriginId = env.OriginId, CarrierId = carrierId ?? env.CarrierId,
            VehicleId = env.VehicleId, DriverId = env.DriverId, CostFinal = costFinal, CostStatus = costStatus,
            TotalUnits = units, TotalDocuments = docs, TotalPackages = packages,
            CreatedAt = DateTime.UtcNow,
        };
        env.Db.Manifests.Add(m);
        await env.Db.SaveChangesAsync();
        return m;
    }

    [Fact]
    public async Task Close_BloqueaConBorradoresDespachadosOSinTarifa()
    {
        var env = ManifestTestEnv.Build();
        await SeedManifestAsync(env, 11, new DateOnly(2026, 9, 1), ManifestStatuses.Draft, null, CostStatuses.NoRate);
        await SeedManifestAsync(env, 22, new DateOnly(2026, 9, 2), ManifestStatuses.Dispatched);
        await SeedManifestAsync(env, 33, new DateOnly(2026, 9, 3), ManifestStatuses.Returned, null, CostStatuses.NoRate);
        await SeedManifestAsync(env, 44, new DateOnly(2026, 9, 4), ManifestStatuses.Annulled, null, CostStatuses.NoRate);
        await SeedManifestAsync(env, 55, new DateOnly(2026, 9, 5));

        var r = await env.Closure.CloseAsync(2026, 9);

        Assert.False(r.Closed);
        var all = string.Join(" | ", r.Blockers);
        Assert.Contains("11", all);
        Assert.Contains("22", all);
        Assert.Contains("33", all);
        Assert.DoesNotContain("44", all);
        Assert.DoesNotContain("55", all);
        Assert.False(await env.Closure.IsClosedAsync(new DateOnly(2026, 9, 10)));
    }

    [Fact]
    public async Task Close_ConCostOverrideNoBloquea_YCongelaYRegistraEventos()
    {
        var env = ManifestTestEnv.Build();
        var a = await SeedManifestAsync(env, 1, new DateOnly(2026, 9, 1));
        var b = await SeedManifestAsync(env, 2, new DateOnly(2026, 9, 2), ManifestStatuses.Returned, 50000, CostStatuses.NoRate);
        b.CostOverride = 50000;
        var x = await SeedManifestAsync(env, 3, new DateOnly(2026, 9, 3), ManifestStatuses.Annulled, null, CostStatuses.NoRate);
        await env.Db.SaveChangesAsync();

        var r = await env.Closure.CloseAsync(2026, 9);

        Assert.True(r.Closed);
        Assert.Empty(r.Blockers);
        Assert.True(await env.Closure.IsClosedAsync(new DateOnly(2026, 9, 30)));
        Assert.False(await env.Closure.IsClosedAsync(new DateOnly(2026, 10, 1)));
        Assert.NotNull((await env.Svc.GetAsync(a.Id))!.CostFrozenAt);
        Assert.Null((await env.Svc.GetAsync(x.Id))!.CostFrozenAt);
        Assert.Equal(2, env.Db.ManifestEvents.Count(e => e.EventType == ManifestEventTypes.PeriodClosed));
    }

    [Fact]
    public async Task Recalculate_PeriodoCerrado_Lanza()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.DraftWithDocsAsync(100);
        await env.Svc.DispatchAsync(id);
        var before = (await env.Svc.GetAsync(id))!.CostFinal;
        Assert.NotNull(before);
        var m = env.Db.Manifests.Single(x => x.Id == id);
        m.Status = ManifestStatuses.Returned;
        await env.Db.SaveChangesAsync();
        Assert.True((await env.Closure.CloseAsync(2026, 11)).Closed);

        // Cambia la tarifa y recalcula: el período cerrado no se toca.
        await env.Tariff.SaveRateCardAsync(new RateCard { CommuneId = env.RancaguaId, Amount = 999999, ValidFrom = new DateOnly(2026, 1, 2) });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Cost.RecalculateAsync(m));
        Assert.Contains("período cerrado", ex.Message);
        Assert.Equal(before, (await env.Svc.GetAsync(id))!.CostFinal);
    }

    [Fact]
    public async Task ChangeVehicle_PeriodoCerrado_Lanza()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.DraftWithDocsAsync(100);
        await env.Svc.DispatchAsync(id);
        env.Db.Manifests.Single(x => x.Id == id).Status = ManifestStatuses.Returned;
        await env.Db.SaveChangesAsync();
        Assert.True((await env.Closure.CloseAsync(2026, 11)).Closed);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => env.Svc.ChangeVehicleOrDriverAsync(id, env.VehicleId, null, env.DriverId, "x"));
        Assert.Contains("período cerrado", ex.Message);
        ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.OverrideCostAsync(id, 5, "x"));
        Assert.Contains("período cerrado", ex.Message);
        ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.AnnulAsync(id, "x"));
        Assert.Contains("período cerrado", ex.Message);
    }

    [Fact]
    public async Task CreateDraft_FechaEnPeriodoCerrado_Lanza()
    {
        var env = ManifestTestEnv.Build();
        await SeedManifestAsync(env, 1, new DateOnly(2026, 9, 1));
        Assert.True((await env.Closure.CloseAsync(2026, 9)).Closed);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => env.Svc.CreateDraftAsync(env.Header(new DateOnly(2026, 9, 15))));
        Assert.Contains("período cerrado", ex.Message);

        // Un borrador de otro mes no puede moverse a un mes cerrado.
        var id = await env.Svc.CreateDraftAsync(env.Header());
        ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => env.Svc.UpdateDraftHeaderAsync(id, env.Header(new DateOnly(2026, 9, 15))));
        Assert.Contains("período cerrado", ex.Message);
    }

    [Fact]
    public async Task Reopen_SoloAdmin_YRegistraEvento()
    {
        var env = ManifestTestEnv.Build();
        var m = await SeedManifestAsync(env, 1, new DateOnly(2026, 9, 1));
        Assert.True((await env.Closure.CloseAsync(2026, 9)).Closed);

        env.User.IsAdmin = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Closure.ReopenAsync(2026, 9, "corrección"));

        env.User.IsAdmin = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Closure.ReopenAsync(2026, 9, " "));
        await env.Closure.ReopenAsync(2026, 9, "corrección");

        Assert.False(await env.Closure.IsClosedAsync(new DateOnly(2026, 9, 1)));
        Assert.Null((await env.Svc.GetAsync(m.Id))!.CostFrozenAt);
        Assert.Contains(env.Db.ManifestEvents, e => e.EventType == ManifestEventTypes.PeriodReopened && e.PayloadJson!.Contains("corrección"));

        // Se puede cerrar de nuevo (misma fila).
        Assert.True((await env.Closure.CloseAsync(2026, 9)).Closed);
        Assert.Single(env.Db.MonthlyClosures);
        Assert.NotNull((await env.Svc.GetAsync(m.Id))!.CostFrozenAt);
    }
}
