using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Adicionales con cantidad (ayudante adicional, punto adicional) ingresados en el manifiesto.</summary>
public class ManifestExtraChargesTests
{
    private static async Task<(long Ayudante, long Punto)> SeedAdicionalesAsync(ManifestTestEnv env, long? carrierId = null)
    {
        var carrier = carrierId ?? env.CarrierId;
        var ayudante = await env.Tariff.SaveSurchargeAsync(new RateSurcharge
        {
            CarrierId = carrier, Name = "Ayudante adicional", ConditionType = "manual_quantity",
            EffectType = "fixed_per_extra_unit", Value = 24455, ValidFrom = new DateOnly(2026, 1, 1), IsActive = true,
        });
        var punto = await env.Tariff.SaveSurchargeAsync(new RateSurcharge
        {
            CarrierId = carrier, Name = "Punto adicional", ConditionType = "manual_quantity",
            EffectType = "fixed_per_extra_unit", Value = 11975, ValidFrom = new DateOnly(2026, 1, 1), IsActive = true,
        });
        return (ayudante, punto);
    }

    [Fact]
    public async Task Borrador_GuardaCantidades_YSeCobranAlDespachar()
    {
        var env = ManifestTestEnv.Build();
        var (ayudante, punto) = await SeedAdicionalesAsync(env);
        var id = await env.DraftWithDocsAsync(1);

        await env.Svc.SetExtraChargesAsync(id, new Dictionary<long, int> { [ayudante] = 1, [punto] = 2 });
        var m = (await env.Svc.GetAsync(id))!;
        Assert.Null(m.CostCalculated); // en borrador no se calcula
        Assert.Contains(m.Events, e => e.EventType == ManifestEventTypes.ExtraChargesChanged);

        var result = await env.Svc.DispatchAsync(id);

        Assert.True(result.Success);
        m = (await env.Svc.GetAsync(id))!;
        Assert.Equal(150000m + 24455m + 23950m, m.CostCalculated);
        Assert.Equal(m.CostCalculated, m.CostFinal);
    }

    [Fact]
    public async Task Despachado_CambiarCantidad_RecalculaElCosto_YDejaEventos()
    {
        var env = ManifestTestEnv.Build();
        var (ayudante, _) = await SeedAdicionalesAsync(env);
        var id = await env.DraftWithDocsAsync(1);
        await env.Svc.DispatchAsync(id);

        await env.Svc.SetExtraChargesAsync(id, new Dictionary<long, int> { [ayudante] = 2 });

        var m = (await env.Svc.GetAsync(id))!;
        Assert.Equal(150000m + 48910m, m.CostCalculated);
        Assert.Contains(m.Events, e => e.EventType == ManifestEventTypes.ExtraChargesChanged
                                       && e.PayloadJson!.Contains("Ayudante adicional"));
        Assert.Contains(m.Events, e => e.EventType == ManifestEventTypes.CostRecalculated);
    }

    [Fact]
    public async Task CantidadCero_QuitaElAdicional()
    {
        var env = ManifestTestEnv.Build();
        var (ayudante, _) = await SeedAdicionalesAsync(env);
        var id = await env.DraftWithDocsAsync(1);
        await env.Svc.DispatchAsync(id);
        await env.Svc.SetExtraChargesAsync(id, new Dictionary<long, int> { [ayudante] = 1 });

        await env.Svc.SetExtraChargesAsync(id, new Dictionary<long, int> { [ayudante] = 0 });

        Assert.Empty(await env.Db.ManifestExtraCharges.Where(x => x.ManifestId == id).ToListAsync());
        Assert.Equal(150000m, (await env.Svc.GetAsync(id))!.CostCalculated);
    }

    [Fact]
    public async Task SinCambios_NoDejaEvento()
    {
        var env = ManifestTestEnv.Build();
        var (ayudante, _) = await SeedAdicionalesAsync(env);
        var id = await env.DraftWithDocsAsync(1);

        await env.Svc.SetExtraChargesAsync(id, new Dictionary<long, int> { [ayudante] = 0 });

        Assert.DoesNotContain((await env.Svc.GetAsync(id))!.Events, e => e.EventType == ManifestEventTypes.ExtraChargesChanged);
    }

    [Fact]
    public async Task AdicionalDeOtroTransportista_SeRechaza()
    {
        var env = ManifestTestEnv.Build();
        var otro = await env.Master.SaveCarrierAsync(new Carrier { Code = "MTO", Name = "MTO", TaxId = "77777777-7" });
        var (ayudanteMto, _) = await SeedAdicionalesAsync(env, otro);
        var id = await env.DraftWithDocsAsync(1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => env.Svc.SetExtraChargesAsync(id, new Dictionary<long, int> { [ayudanteMto] = 1 }));
        Assert.Contains("no aplica", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    public async Task CantidadFueraDeRango_SeRechaza(int cantidad)
    {
        var env = ManifestTestEnv.Build();
        var (ayudante, _) = await SeedAdicionalesAsync(env);
        var id = await env.DraftWithDocsAsync(1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => env.Svc.SetExtraChargesAsync(id, new Dictionary<long, int> { [ayudante] = cantidad }));
    }

    [Fact]
    public async Task ManifiestoAnulado_SeRechaza()
    {
        var env = ManifestTestEnv.Build();
        var (ayudante, _) = await SeedAdicionalesAsync(env);
        var id = await env.DraftWithDocsAsync(1);
        await env.Svc.AnnulAsync(id, "error de armado");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => env.Svc.SetExtraChargesAsync(id, new Dictionary<long, int> { [ayudante] = 1 }));
        Assert.Contains("anulado", ex.Message);
    }

    [Fact]
    public async Task Lista_SoloDelTransportista_ConSuCantidad_IncluyeInactivoConCantidad()
    {
        var env = ManifestTestEnv.Build();
        var (ayudante, punto) = await SeedAdicionalesAsync(env);
        var otro = await env.Master.SaveCarrierAsync(new Carrier { Code = "MTO", Name = "MTO", TaxId = "77777777-7" });
        await SeedAdicionalesAsync(env, otro);
        var id = await env.DraftWithDocsAsync(1);
        await env.Svc.SetExtraChargesAsync(id, new Dictionary<long, int> { [punto] = 3 });
        var p = await env.Db.RateSurcharges.SingleAsync(s => s.Id == punto);
        p.IsActive = false;
        await env.Db.SaveChangesAsync();

        var rows = await env.Svc.ListExtraChargesAsync(id);

        Assert.Equal(new[] { ayudante, punto }.OrderBy(x => x), rows.Select(r => r.RateSurchargeId).OrderBy(x => x));
        Assert.Equal(3, rows.Single(r => r.RateSurchargeId == punto).Quantity);
        Assert.Equal(0, rows.Single(r => r.RateSurchargeId == ayudante).Quantity);
    }

    [Fact]
    public async Task EliminarRecargoUsadoEnManifiesto_SeBloquea()
    {
        var env = ManifestTestEnv.Build();
        var (ayudante, _) = await SeedAdicionalesAsync(env);
        var id = await env.DraftWithDocsAsync(1);
        await env.Svc.SetExtraChargesAsync(id, new Dictionary<long, int> { [ayudante] = 1 });

        var result = await env.Tariff.DeleteSurchargeAsync(ayudante);

        Assert.False(result.Deleted);
    }
}
