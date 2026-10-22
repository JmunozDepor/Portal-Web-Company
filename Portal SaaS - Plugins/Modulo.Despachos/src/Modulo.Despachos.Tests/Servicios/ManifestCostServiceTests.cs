using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

public class ManifestCostServiceTests
{
    private static async Task<Manifest> LoadAsync(ManifestTestEnv env, Guid id) => (await env.Svc.GetAsync(id))!;

    [Fact]
    public async Task Recalculate_DosComunas_BaseEsLaMayor_YCuentasPorLocalidad()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(1, commune: "Rancagua");
        env.AddSap(2, commune: "Curicó");
        env.AddSap(3, commune: "Curicó");
        var id = await env.Svc.CreateDraftAsync(env.Header());
        foreach (var f in new long[] { 1, 2, 3 })
            await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, f));

        var m = await LoadAsync(env, id);
        var result = await env.Cost.RecalculateAsync(m);

        Assert.Equal("ok", result.Status);
        Assert.Equal(220000m, m.CostCalculated);
        Assert.Equal(220000m, m.CostFinal);
        Assert.Equal("ok", m.CostStatus);
        var lines = await env.Db.ManifestCostLines.Where(l => l.ManifestId == id && l.Kind == "base").ToListAsync();
        Assert.Equal(2, lines.Count);
        var curico = lines.Single(l => l.CommuneId == env.CuricoId);
        Assert.Equal(220000m, curico.Amount);
        Assert.Equal(2, curico.Documents);
        Assert.Equal(20m, curico.Units);
        Assert.Equal(8, curico.Packages);
        Assert.Equal(1, lines.Single(l => l.CommuneId == env.RancaguaId).Documents);
        Assert.Equal(curico.RateCardId, m.RateCardId);
    }

    [Fact]
    public async Task Recalculate_ReemplazaLasLineasAnteriores()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.DraftWithDocsAsync(1);
        var m = await LoadAsync(env, id);
        await env.Cost.RecalculateAsync(m);
        await env.Cost.RecalculateAsync(m);
        Assert.Single(await env.Db.ManifestCostLines.Where(l => l.ManifestId == id).ToListAsync());
    }

    [Fact]
    public async Task Estimate_CalculaElCosto_SinGuardarNada()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.DraftWithDocsAsync(1);
        var m = await LoadAsync(env, id);

        var result = await env.Cost.EstimateAsync(m);

        Assert.Equal("ok", result.Status);
        Assert.Equal(150000m, result.Total);
        Assert.NotEmpty(result.Lines);
        Assert.Empty(await env.Db.ManifestCostLines.Where(l => l.ManifestId == id).ToListAsync());
        var stored = await env.Db.Manifests.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Null(stored.CostCalculated);
        Assert.Null(m.CostCalculated);
    }

    [Fact]
    public async Task Recalculate_ConOverride_FinalEsElOverride_YCalculadoSeConserva()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.DraftWithDocsAsync(1);
        var m = await LoadAsync(env, id);
        m.CostOverride = 90000m;
        m.CostOverrideReason = "Negociado";
        await env.Cost.RecalculateAsync(m);
        Assert.Equal(150000m, m.CostCalculated);
        Assert.Equal(90000m, m.CostFinal);
    }

    [Fact]
    public async Task Recalculate_ComunaSinResolver_NoRate_YCalculadoNulo()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(1, commune: "Rancagua");
        env.AddSap(2, commune: "Lugar Desconocido");
        var id = await env.Svc.CreateDraftAsync(env.Header());
        await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 1));
        await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 2));
        var m = await LoadAsync(env, id);
        var result = await env.Cost.RecalculateAsync(m);
        Assert.Equal("no_rate", result.Status);
        Assert.Equal("no_rate", m.CostStatus);
        Assert.Null(m.CostCalculated);
        Assert.Null(m.CostFinal);
    }

    [Fact]
    public async Task Recalculate_SinTarifaConOverride_FinalEsElOverride()
    {
        var env = ManifestTestEnv.Build(seedRates: false);
        var id = await env.DraftWithDocsAsync(1);
        var m = await LoadAsync(env, id);
        m.CostOverride = 70000m;
        await env.Cost.RecalculateAsync(m);
        Assert.Equal("no_rate", m.CostStatus);
        Assert.Null(m.CostCalculated);
        Assert.Equal(70000m, m.CostFinal);
    }

    [Fact]
    public async Task Recalculate_TarifaAmbigua_CalculadoNulo()
    {
        var env = ManifestTestEnv.Build();
        await env.Tariff.SaveRateCardAsync(new RateCard { CommuneId = env.RancaguaId, Amount = 1, ValidFrom = new DateOnly(2026, 1, 1) });
        var id = await env.DraftWithDocsAsync(1);
        var m = await LoadAsync(env, id);
        await env.Cost.RecalculateAsync(m);
        Assert.Equal("ambiguous", m.CostStatus);
        Assert.Null(m.CostCalculated);
    }
}
