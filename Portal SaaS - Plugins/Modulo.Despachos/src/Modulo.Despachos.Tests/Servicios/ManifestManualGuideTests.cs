using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>La guía manual viaja en el manifiesto como documento: escaneo GM-, selector, quitar y anular.</summary>
public class ManifestManualGuideTests
{
    private static async Task<(ManifestTestEnv Env, Guid ManifestId, Guid GuideId)> SetupAsync()
    {
        var env = ManifestTestEnv.Build();
        var guide = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        var manifest = await env.Svc.CreateDraftAsync(env.Header());
        return (env, manifest, guide);
    }

    private static Task<AddDocumentResult> Scan(ManifestTestEnv env, Guid manifestId, string code) =>
        env.Svc.AddDocumentAsync(manifestId, new DocumentCapture(code, null, null));

    [Fact]
    public async Task Escanear_Gm_AgregaComoDocumento()
    {
        var (env, mid, gid) = await SetupAsync();

        var r = await Scan(env, mid, "GM-000001");

        Assert.Equal(AddDocumentOutcomes.Added, r.Outcome);
        var d = Assert.Single((await env.Svc.GetAsync(mid))!.Documents);
        Assert.Equal((DocumentKinds.ManualGuide, 52, 0L, gid, 1m, 595000m), (d.DocumentKind, d.SiiDocumentType, d.Folio, d.TransportGuideId, d.Units, d.TotalAmount));
        Assert.Equal(("Freddi Fuentes Norambuena", "17183714-6", env.RancaguaId), (d.CustomerName, d.CustomerTaxId, d.DestinationCommuneId));
        var g = (await env.ManualGuides.GetAsync(gid))!;
        Assert.Equal((TransportGuideStatuses.InManifest, ManualTransportModes.Manifest), (g.Status, g.TransportMode));
    }

    [Fact]
    public async Task Escanear_Gm_DosVeces_Duplicado()
    {
        var (env, mid, _) = await SetupAsync();
        await Scan(env, mid, "GM-000001");
        Assert.Equal(AddDocumentOutcomes.DuplicateInThis, (await Scan(env, mid, "gm-000001")).Outcome);
    }

    [Fact]
    public async Task Escanear_Gm_EnOtroManifiesto_Rechaza()
    {
        var (env, mid, _) = await SetupAsync();
        await Scan(env, mid, "GM-000001");
        var other = await env.Svc.CreateDraftAsync(env.Header());
        var r = await Scan(env, other, "GM-000001");
        Assert.Equal(AddDocumentOutcomes.InOtherManifest, r.Outcome);
        Assert.Equal(1, r.OtherManifestNumber);
    }

    [Fact]
    public async Task Escanear_Gm_Inexistente_NoEncontrada()
    {
        var (env, mid, _) = await SetupAsync();
        Assert.Equal(AddDocumentOutcomes.NotFound, (await Scan(env, mid, "GM-000099")).Outcome);
    }

    [Fact]
    public async Task Escanear_Gm_Emitida_Rechaza()
    {
        var (env, mid, gid) = await SetupAsync();
        await env.ManualGuides.EmitAsync(gid);
        var r = await Scan(env, mid, "GM-000001");
        Assert.Equal(AddDocumentOutcomes.NotFound, r.Outcome);
        Assert.Contains("ya fue emitida", r.Warnings[0]);
    }

    [Fact]
    public async Task Escanear_Gm_Anulada_Rechaza()
    {
        var (env, mid, gid) = await SetupAsync();
        await env.ManualGuides.AnnulAsync(gid, "error");
        Assert.Equal(AddDocumentOutcomes.Canceled, (await Scan(env, mid, "GM-000001")).Outcome);
    }

    [Fact]
    public async Task Quitar_VuelveABorrador()
    {
        var (env, mid, gid) = await SetupAsync();
        await Scan(env, mid, "GM-000001");
        var doc = (await env.Svc.GetAsync(mid))!.Documents.Single();

        await env.Svc.RemoveDocumentAsync(mid, doc.Id);

        var g = (await env.ManualGuides.GetAsync(gid))!;
        Assert.Equal((TransportGuideStatuses.Draft, ManualTransportModes.Manual), (g.Status, g.TransportMode));
    }

    [Fact]
    public async Task QuitarTodos_VuelveABorrador()
    {
        var (env, mid, gid) = await SetupAsync();
        await Scan(env, mid, "GM-000001");
        await env.Svc.RemoveAllDocumentsAsync(mid);
        Assert.Equal(TransportGuideStatuses.Draft, (await env.ManualGuides.GetAsync(gid))!.Status);
    }

    [Fact]
    public async Task AnularManifiestoBorrador_VuelveABorrador_YSePuedeAgregarAOtro()
    {
        var (env, mid, gid) = await SetupAsync();
        await Scan(env, mid, "GM-000001");
        await env.Svc.AnnulAsync(mid, "se cayó el viaje");
        Assert.Equal(TransportGuideStatuses.Draft, (await env.ManualGuides.GetAsync(gid))!.Status);

        var other = await env.Svc.CreateDraftAsync(env.Header());
        Assert.Equal(AddDocumentOutcomes.Added, (await Scan(env, other, "GM-000001")).Outcome);
    }

    [Fact]
    public async Task Selector_PorId_Agrega()
    {
        var (env, mid, gid) = await SetupAsync();
        var r = await env.Svc.AddManualGuideByIdAsync(mid, gid);
        Assert.Equal(AddDocumentOutcomes.Added, r.Outcome);
        Assert.Equal(CaptureMethods.Manual, (await env.Svc.GetAsync(mid))!.Documents.Single().CaptureMethod);
    }

    [Fact]
    public async Task DosGuiasManuales_NoSeConsideranDuplicadas()
    {
        var (env, mid, _) = await SetupAsync();
        await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        Assert.Equal(AddDocumentOutcomes.Added, (await Scan(env, mid, "GM-000001")).Outcome);
        Assert.Equal(AddDocumentOutcomes.Added, (await Scan(env, mid, "GM-000002")).Outcome);
        Assert.Equal(2, (await env.Svc.GetAsync(mid))!.Documents.Count);
    }

    [Fact]
    public async Task GuiaEnManifiesto_NoSeEmiteSola_NiSeAnula()
    {
        var (env, mid, gid) = await SetupAsync();
        await Scan(env, mid, "GM-000001");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.ManualGuides.EmitAsync(gid));
        Assert.Contains("manifiesto N° 1", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.ManualGuides.AnnulAsync(gid, "x"));
    }
}
