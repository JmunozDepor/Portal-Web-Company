using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Agrupaciones en el manifiesto: escanear la hoja AT, bloqueo del LPN suelto y ciclo de estados.</summary>
public class ManifestDispatchGroupTests
{
    private static readonly DateOnly Desde = DispatchGroupServiceTests.Desde;

    /// <summary>Entorno con una agrupación AT-000001 de TDA01 (501, 502) y un borrador de manifiesto.</summary>
    private static async Task<(ManifestTestEnv Env, DispatchGroup Group, Guid ManifestId)> ArrangeAsync(int[]? entries = null)
    {
        var env = await DispatchGroupServiceTests.EnvAsync();
        var g = Assert.Single(await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, entries ?? new[] { 501, 502 }));
        var id = await env.Svc.CreateDraftAsync(env.Header());
        return (env, g, id);
    }

    private static Task<AddDocumentResult> Scan(ManifestTestEnv env, Guid id, string text) =>
        env.Svc.AddDocumentAsync(id, new DocumentCapture(text, null, null));

    private static async Task<string> GroupStatus(ManifestTestEnv env, long groupId) =>
        (await env.Db.DispatchGroups.AsNoTracking().SingleAsync(g => g.Id == groupId)).Status;

    [Fact]
    public async Task EscanearHoja_AgregaTodas_YPasaAEnManifiesto()
    {
        var (env, g, id) = await ArrangeAsync();

        var r = await Scan(env, id, "AT-000001");

        Assert.Equal(AddDocumentOutcomes.GroupAdded, r.Outcome);
        Assert.Contains("2 transferencias agregadas", r.Warnings[0]);
        var docs = (await env.Svc.GetAsync(id))!.Documents;
        Assert.Equal(new[] { 501, 502 }, docs.Select(d => d.SapDocEntry).OrderBy(x => x));
        Assert.All(docs, d => Assert.Equal(g.Id, d.DispatchGroupId));
        Assert.Equal(DispatchGroupStatuses.InManifest, await GroupStatus(env, g.Id));
    }

    [Fact]
    public async Task EscanearHoja_TransferenciaAnuladaDespues_EntraElResto()
    {
        var (env, g, id) = await ArrangeAsync();
        var i = env.Sap.Transfers.FindIndex(t => t.DocEntry == 502);
        env.Sap.Transfers[i] = env.Sap.Transfers[i] with { IsCanceled = true };

        var r = await Scan(env, id, "at-1");

        Assert.Equal(AddDocumentOutcomes.GroupAdded, r.Outcome);
        Assert.Equal(501, Assert.Single((await env.Svc.GetAsync(id))!.Documents).SapDocEntry);
        Assert.Contains(r.Warnings, w => w.Contains("10502") && w.Contains("ya no está vigente"));
    }

    [Fact]
    public async Task EscanearHoja_EnOtroManifiesto_Rechaza()
    {
        var (env, _, id) = await ArrangeAsync();
        await Scan(env, id, "AT-000001");
        var other = await env.Svc.CreateDraftAsync(env.Header());

        var r = await Scan(env, other, "AT-000001");

        Assert.Equal(AddDocumentOutcomes.InOtherManifest, r.Outcome);
        Assert.Contains("manifiesto N°", r.Warnings[0]);
        Assert.Empty((await env.Svc.GetAsync(other))!.Documents);
    }

    [Fact]
    public async Task EscanearHoja_DosVecesEnElMismo_Duplicado()
    {
        var (env, _, id) = await ArrangeAsync();
        await Scan(env, id, "AT-000001");
        Assert.Equal(AddDocumentOutcomes.DuplicateInThis, (await Scan(env, id, "AT-000001")).Outcome);
    }

    [Fact]
    public async Task EscanearHoja_Anulada_Rechaza()
    {
        var (env, g, id) = await ArrangeAsync();
        await env.Groups.AnnulAsync(g.Id, "error");

        var r = await Scan(env, id, "AT-000001");

        Assert.Equal(AddDocumentOutcomes.Canceled, r.Outcome);
        Assert.Contains("anulada", r.Warnings[0]);
    }

    [Fact]
    public async Task EscanearHoja_Inexistente_NotFound()
    {
        var (env, _, id) = await ArrangeAsync();
        var r = await Scan(env, id, "AT-000099");
        Assert.Equal(AddDocumentOutcomes.NotFound, r.Outcome);
        Assert.Contains("AT-000099", r.Warnings[0]);
    }

    [Fact]
    public async Task EscanearHoja_FechaDistinta_Aviso()
    {
        var env = await DispatchGroupServiceTests.EnvAsync();
        await env.Groups.GenerateAsync(ManifestTestEnv.Fecha.AddDays(1), Desde, new[] { 501 });
        var id = await env.Svc.CreateDraftAsync(env.Header());

        var r = await Scan(env, id, "AT-000001");

        Assert.Equal(AddDocumentOutcomes.GroupAdded, r.Outcome);
        Assert.Contains(r.Warnings, w => w.Contains("La agrupación es para el 11-11-2026"));
    }

    [Fact]
    public async Task LpnSuelto_DeAgrupacion_Rechaza()
    {
        var (env, _, id) = await ArrangeAsync();

        var r = await Scan(env, id, "CTBO501");

        Assert.Equal(AddDocumentOutcomes.InDispatchGroup, r.Outcome);
        Assert.Contains("AT-000001", r.Warnings[0]);
        Assert.Contains("escanee la hoja", r.Warnings[0]);
    }

    [Fact]
    public async Task AgregarPendientes_OmiteLasDeAgrupacion()
    {
        var (env, _, id) = await ArrangeAsync(new[] { 501 });

        var r = await env.Svc.AddPendingTransfersAsync(id, "TDA01", Desde);

        Assert.Equal(1, r.Added);
        Assert.Contains(r.Skipped, s => s.Contains("AT-000001"));
    }

    [Fact]
    public async Task QuitarUltimaTransferencia_VuelveAArmada()
    {
        var (env, g, id) = await ArrangeAsync(new[] { 501 });
        await Scan(env, id, "AT-000001");
        var doc = Assert.Single((await env.Svc.GetAsync(id))!.Documents);

        await env.Svc.RemoveDocumentAsync(id, doc.Id);

        Assert.Equal(DispatchGroupStatuses.Armed, await GroupStatus(env, g.Id));
    }

    [Fact]
    public async Task QuitarUnaDeDos_SigueEnManifiesto()
    {
        var (env, g, id) = await ArrangeAsync();
        await Scan(env, id, "AT-000001");
        var doc = (await env.Svc.GetAsync(id))!.Documents.First();

        await env.Svc.RemoveDocumentAsync(id, doc.Id);

        Assert.Equal(DispatchGroupStatuses.InManifest, await GroupStatus(env, g.Id));
    }

    [Fact]
    public async Task QuitarAgrupacion_SacaTodas_VuelveAArmada()
    {
        var (env, g, id) = await ArrangeAsync();
        await Scan(env, id, "AT-000001");

        await env.Svc.RemoveDispatchGroupAsync(id, g.Id);

        Assert.Empty((await env.Svc.GetAsync(id))!.Documents);
        Assert.Equal(DispatchGroupStatuses.Armed, await GroupStatus(env, g.Id));
    }

    [Fact]
    public async Task QuitarTodos_VaciaElBorrador_YLasAgrupacionesVuelvenAArmada()
    {
        var (env, g, id) = await ArrangeAsync(new[] { 501 });
        await Scan(env, id, "AT-000001");
        Assert.Equal("added", (await Scan(env, id, "CTBO503")).Outcome);

        var removed = await env.Svc.RemoveAllDocumentsAsync(id);

        Assert.Equal(2, removed);
        Assert.Empty((await env.Svc.GetAsync(id))!.Documents);
        Assert.Equal(DispatchGroupStatuses.Armed, await GroupStatus(env, g.Id));
        Assert.Equal(2, env.Db.ManifestEvents.Count(e => e.ManifestId == id && e.EventType == ManifestEventTypes.DocumentRemoved));
    }

    [Fact]
    public async Task QuitarTodos_ManifiestoDespachado_Lanza()
    {
        var (env, _, id) = await ArrangeAsync(new[] { 501 });
        await Scan(env, id, "AT-000001");
        Assert.True((await env.Svc.DispatchAsync(id)).Success);

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.RemoveAllDocumentsAsync(id));
    }

    [Fact]
    public async Task AnularManifiesto_VuelveAArmada()
    {
        var (env, g, id) = await ArrangeAsync();
        await Scan(env, id, "AT-000001");

        await env.Svc.AnnulAsync(id, "se suspende");

        Assert.Equal(DispatchGroupStatuses.Armed, await GroupStatus(env, g.Id));
    }

    /// <summary>Despacha la agrupación AT-000001 (501, 502) y registra el retorno con 501 no entregada.</summary>
    private static async Task<ManifestTestEnv> DespachadaConNoEntregadaAsync()
    {
        var (env, _, id) = await ArrangeAsync();
        await Scan(env, id, "AT-000001");
        Assert.True((await env.Svc.DispatchAsync(id)).Success);
        var reason = await env.Master.SaveRejectionReasonAsync(new RejectionReason
            { Code = "ND", Name = "Tienda cerrada", AppliesTo = RejectionAppliesTo.NotDelivered, IsActive = true });
        var docs = (await env.Svc.GetAsync(id))!.Documents;
        await env.Svc.RegisterReturnAsync(id, docs.Select(d => new DocumentReturnInput(d.Id,
            d.SapDocEntry == 501 ? DeliveryStatuses.NotDelivered : DeliveryStatuses.Delivered,
            d.SapDocEntry == 501 ? reason : null, null)).ToList(), null);
        return env;
    }

    [Fact]
    public async Task NoEntregadaDeAgrupacionDespachada_SePuedeVolverADespacharSuelta()
    {
        var env = await DespachadaConNoEntregadaAsync();
        var next = await env.Svc.CreateDraftAsync(env.Header());

        var r = await Scan(env, next, "CTBO501");

        Assert.Equal(AddDocumentOutcomes.Added, r.Outcome);
    }

    [Fact]
    public async Task NoEntregadaDeAgrupacionDespachada_VuelveACandidatos()
    {
        var env = await DespachadaConNoEntregadaAsync();

        var c = await env.Groups.ListCandidatesAsync(Desde);

        Assert.Contains(501, c.SelectMany(x => x.Transfers).Select(t => t.DocEntry));
    }

    [Fact]
    public async Task ConGuias_PendientesQuedaEnManifiesto_AlEmitirPasaADespachada()
    {
        var (env, g, id) = await ArrangeAsync();
        await env.EnableGuidesAsync();
        env.Sap.Lines[501] = new SapDocumentLines(null, "Rancagua", new[] { new SapDocumentLine(0, "A", "Zapatilla", 8, "UN", 1m) });
        env.Sap.Lines[502] = new SapDocumentLines(null, "Rancagua", new[] { new SapDocumentLine(0, "B", "Polera", 9, "UN", 1m) });
        await env.Svc.UpdateDraftHeaderAsync(id, env.Header(arrival: ManifestTestEnv.Fecha));
        await Scan(env, id, "AT-000001");
        env.Dte.Down = true;

        await env.Svc.DispatchAsync(id);

        Assert.Equal(ManifestStatuses.PendingGuides, (await env.Svc.GetAsync(id))!.Status);
        Assert.Equal(DispatchGroupStatuses.InManifest, await GroupStatus(env, g.Id));

        env.Dte.Down = false;
        Assert.True((await env.Svc.RetryGuidesAsync(id)).Success);
        Assert.Equal(DispatchGroupStatuses.Dispatched, await GroupStatus(env, g.Id));
    }

    [Fact]
    public async Task Despachar_PasaADespachada()
    {
        var (env, g, id) = await ArrangeAsync();
        await Scan(env, id, "AT-000001");

        var r = await env.Svc.DispatchAsync(id);

        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Equal(DispatchGroupStatuses.Dispatched, await GroupStatus(env, g.Id));
    }
}
