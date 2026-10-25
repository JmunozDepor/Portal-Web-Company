using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Dominio.Guias;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Las guías manuales del manifiesto se emiten al despacharlo, con el transporte del manifiesto.</summary>
public class ManifestManualGuideEmissionTests
{
    private static async Task<(ManifestTestEnv Env, Guid ManifestId, Guid GuideId)> ManifestWithManualGuideAsync(
        bool arrival = true, params long[] invoices)
    {
        var env = ManifestTestEnv.Build();
        var guide = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env, notes: "Frágil"));
        var manifest = invoices.Length > 0
            ? await env.GuideReadyDraftAsync(invoices)
            : await env.Svc.CreateDraftAsync(env.Header(arrival: arrival ? ManifestTestEnv.Fecha : null));
        Assert.Equal(AddDocumentOutcomes.Added, (await env.Svc.AddManualGuideByIdAsync(manifest, guide)).Outcome);
        return (env, manifest, guide);
    }

    [Fact]
    public async Task Despachar_SoloGuiaManual_SinGuidesEnabled_EmiteYDespacha()
    {
        var (env, mid, gid) = await ManifestWithManualGuideAsync();

        var r = await env.Svc.DispatchAsync(mid);

        Assert.True(r.Success, string.Join(" ", r.Errors));
        var m = (await env.Svc.GetAsync(mid))!;
        Assert.Equal(ManifestStatuses.Dispatched, m.Status);
        Assert.Equal(500L, m.Documents.Single().Folio);
        var sent = Assert.Single(env.Dte.Calls);
        Assert.Equal(("AB1234", "12345678-5", "76543210-3"), (sent.Document.Transport!.Plate, sent.Document.Transport.Driver!.TaxId, sent.Document.Transport.CarrierTaxId));
        Assert.Equal("Avenida Circunvalación 1055", sent.Document.Transport.DestinationAddress);
        Assert.StartsWith("OBS: ", sent.Document.Lines[^1].Name);
        Assert.Equal(ManifestTestEnv.Fecha, sent.Document.IdDoc.IssueDate);
        var g = (await env.ManualGuides.GetAsync(gid))!;
        Assert.Equal((TransportGuideStatuses.Issued, 500L), (g.Status, g.Folio));
    }

    [Fact]
    public async Task Despachar_FacturaYGuiaManual_DosEmisiones()
    {
        var (env, mid, _) = await ManifestWithManualGuideAsync(true, 901);
        await env.EnableGuidesAsync();

        var r = await env.Svc.DispatchAsync(mid);

        Assert.True(r.Success, string.Join(" ", r.Errors));
        Assert.Equal(2, env.Dte.Calls.Count);
        Assert.All((await env.Svc.GetAsync(mid))!.Documents, d => Assert.True(d.Folio > 0));
        Assert.NotEmpty(await env.Guides.GetPdfAsync(mid, null));
    }

    [Fact]
    public async Task Despachar_ProveedorCaido_QuedaGuiasPendientes_YReintentoEmite()
    {
        var (env, mid, gid) = await ManifestWithManualGuideAsync();
        env.Dte.Down = true;

        var r = await env.Svc.DispatchAsync(mid);
        Assert.False(r.Success);
        Assert.Equal(ManifestStatuses.PendingGuides, (await env.Svc.GetAsync(mid))!.Status);
        Assert.Equal(TransportGuideStatuses.Pending, (await env.ManualGuides.GetAsync(gid))!.Status);

        env.Dte.Down = false;
        Assert.True((await env.Svc.RetryGuidesAsync(mid)).Success);
        Assert.Equal(ManifestStatuses.Dispatched, (await env.Svc.GetAsync(mid))!.Status);
        Assert.Single(env.Dte.Calls.Select(c => c.EmissionKey).Distinct());
    }

    [Fact]
    public async Task AnularConGuiasPendientes_GuiaManualVuelveABorradorConLlaveNueva()
    {
        var (env, mid, gid) = await ManifestWithManualGuideAsync();
        var originalKey = (await env.ManualGuides.GetAsync(gid))!.EmissionKey;
        env.Dte.Down = true;
        await env.Svc.DispatchAsync(mid);

        await env.Svc.AnnulAsync(mid, "se suspendió el viaje");

        var g = (await env.ManualGuides.GetAsync(gid))!;
        Assert.Equal(TransportGuideStatuses.Draft, g.Status);
        Assert.NotEqual(originalKey, g.EmissionKey);
        Assert.Contains(originalKey, env.Dte.Abandoned);

        env.Dte.Down = false;
        Assert.Equal(TransportGuideStatuses.Issued, (await env.ManualGuides.EmitAsync(gid)).Status);
    }

    [Fact]
    public async Task Despachar_SinFechaDeLlegada_NoCambiaNada()
    {
        var (env, mid, gid) = await ManifestWithManualGuideAsync(arrival: false);

        var r = await env.Svc.DispatchAsync(mid);

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("fecha de llegada"));
        Assert.Equal(ManifestStatuses.Draft, (await env.Svc.GetAsync(mid))!.Status);
        Assert.Equal(TransportGuideStatuses.InManifest, (await env.ManualGuides.GetAsync(gid))!.Status);
        Assert.Empty(env.Dte.Calls);
    }

    [Fact]
    public async Task VistaPrevia_IncluyeGuiaManual()
    {
        var (env, mid, _) = await ManifestWithManualGuideAsync();
        var preview = await env.Guides.PreviewAsync(mid);
        Assert.True(preview.Enabled);
        Assert.Empty(preview.Errors);
        Assert.Contains(preview.Guides, g => g.ReceiverName == "Freddi Fuentes Norambuena");
    }
}
