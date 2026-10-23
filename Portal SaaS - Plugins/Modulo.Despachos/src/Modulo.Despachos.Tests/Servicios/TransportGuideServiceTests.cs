using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;
using PdfSharp.Pdf.IO;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.Despachos.Tests.Servicios;

public class TransportGuideServiceTests
{
    /// <summary>Guías habilitadas + borrador con dos facturas a destinos distintos (Rancagua y Curicó) = 2 guías.</summary>
    private static async Task<(ManifestTestEnv Env, Guid Id)> TwoGuidesAsync()
    {
        var env = ManifestTestEnv.Build();
        await env.EnableGuidesAsync();
        env.AddSap(1);
        env.AddSap(2, commune: "Curicó");
        var id = await env.GuideReadyDraftAsync(1, 2);
        return (env, id);
    }

    private static Task<List<TransportGuide>> GuidesOf(ManifestTestEnv env, Guid id) =>
        env.Db.TransportGuides.Where(g => g.ManifestId == id).OrderBy(g => g.Sequence).ToListAsync();

    [Fact]
    public async Task Preview_Borrador_PlanificaConMaestrosVigentes()
    {
        var (env, id) = await TwoGuidesAsync();
        var p = await env.Guides.PreviewAsync(id);

        Assert.True(p.Enabled);
        Assert.Empty(p.Errors);
        Assert.Equal(2, p.Guides.Count);
        Assert.Contains("Curicó", p.Guides[1].Destination);
        // RUT y código de socio de negocio para buscar al receptor en SAP.
        Assert.All(p.Guides, g => Assert.Equal("11111111-1", g.ReceiverTaxId));
        Assert.All(p.Guides, g => Assert.Equal("C001", g.CustomerCodes));
        Assert.Empty(await GuidesOf(env, id)); // la vista previa no persiste nada
    }

    [Fact]
    public async Task Preview_SinConfiguracionDte_Error()
    {
        var (env, id) = await TwoGuidesAsync();
        env.Dte.Configured = false;
        var p = await env.Guides.PreviewAsync(id);
        Assert.Contains(p.Errors, e => e.Contains("documentos electrónicos"));
    }

    [Fact]
    public async Task Preview_RutEmisorDistintoAlDeDespachos_Error()
    {
        var (env, id) = await TwoGuidesAsync();
        env.Dte.IssuerTaxId = "76030680-0";
        var p = await env.Guides.PreviewAsync(id);
        Assert.Contains(p.Errors, e => e.Contains("76030680-0") && e.Contains(ManifestTestEnv.IssuerRut));
    }

    [Fact]
    public async Task Dispatch_ConGuias_EmiteYDespacha()
    {
        var (env, id) = await TwoGuidesAsync();

        var r = await env.Svc.DispatchAsync(id);

        Assert.True(r.Success, string.Join(" | ", r.Errors));
        var m = (await env.Svc.GetAsync(id))!;
        Assert.Equal(ManifestStatuses.Dispatched, m.Status);
        Assert.All(m.Documents, d => Assert.Equal(DeliveryStatuses.Dispatched, d.DeliveryStatus));
        var guides = await GuidesOf(env, id);
        Assert.Equal(new long?[] { 500, 501 }, guides.Select(g => g.Folio));
        Assert.All(guides, g => Assert.Equal(TransportGuideStatuses.Issued, g.Status));
        Assert.Equal(2, await env.Db.ManifestEvents.CountAsync(e => e.ManifestId == id && e.EventType == ManifestEventTypes.GuideIssued));
        var sent = env.Dte.Calls[0];
        Assert.Equal("despachos", sent.SourceModule);
        Assert.Equal($"manifest:{m.Number}/guide:1", sent.SourceRef);
        Assert.Equal("AB1234", sent.Document.Transport!.Plate);
        Assert.Equal(new TimeOnly(12, 0), sent.Document.Transport.DepartureTime);
    }

    [Fact]
    public async Task Dispatch_ConGuias_ErrorPlan_NoCambiaNada()
    {
        var env = ManifestTestEnv.Build();
        await env.EnableGuidesAsync();
        var id = await env.DraftWithDocsAsync(1); // sin fecha de llegada ni líneas SAP

        var r = await env.Svc.DispatchAsync(id);

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("fecha de llegada"));
        Assert.Equal(ManifestStatuses.Draft, (await env.Svc.GetAsync(id))!.Status);
        Assert.Empty(await GuidesOf(env, id));
        Assert.Empty(env.Dte.Calls);
    }

    [Fact]
    public async Task Dispatch_ProveedorCaido_QuedaGuiasPendientesYAvisa()
    {
        var (env, id) = await TwoGuidesAsync();
        env.Dte.Down = true;

        var r = await env.Svc.DispatchAsync(id);

        Assert.False(r.Success);
        Assert.Contains(r.Warnings, w => w.Contains("no debe salir"));
        var m = (await env.Svc.GetAsync(id))!;
        Assert.Equal(ManifestStatuses.PendingGuides, m.Status);
        Assert.NotNull(m.DepartedAt);
        Assert.All(await GuidesOf(env, id), g => Assert.Equal(TransportGuideStatuses.Pending, g.Status));
        Assert.Contains(env.Notifications.Enqueued, n => n.UserId == env.User.UserId && n.Message.Contains("sin emitir"));
        Assert.Equal(1, await env.Db.ManifestEvents.CountAsync(e => e.ManifestId == id && e.EventType == ManifestEventTypes.GuideFailed));
    }

    [Fact]
    public async Task RetryGuides_SoloEmiteLasPendientes()
    {
        var (env, id) = await TwoGuidesAsync();
        await env.Svc.DispatchAsync(id);           // ambas emitidas...
        var guides = await GuidesOf(env, id);
        // ...simula que la segunda quedó pendiente
        guides[1].Status = TransportGuideStatuses.Pending;
        guides[1].Folio = null;
        env.Dte.Emissions.Remove(guides[1].EmissionKey);
        var m = (await env.Svc.GetAsync(id))!;
        m.Status = ManifestStatuses.PendingGuides;
        await env.Db.SaveChangesAsync();
        env.Dte.Calls.Clear();

        var r = await env.Svc.RetryGuidesAsync(id);

        Assert.True(r.Success, string.Join(" | ", r.Errors));
        Assert.Equal(guides[1].EmissionKey, Assert.Single(env.Dte.Calls).EmissionKey);
        Assert.Equal(ManifestStatuses.Dispatched, (await env.Svc.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task RetryGuides_MismaEmissionKey_YMismoContenido()
    {
        var (env, id) = await TwoGuidesAsync();
        env.Dte.Down = true;
        await env.Svc.DispatchAsync(id);
        var firstCalls = env.Dte.Calls.ToList();
        env.Dte.Down = false;

        var r = await env.Svc.RetryGuidesAsync(id);

        Assert.True(r.Success, string.Join(" | ", r.Errors));
        var retried = env.Dte.Calls.Skip(firstCalls.Count).ToList();
        Assert.Equal(firstCalls.Select(c => c.EmissionKey), retried.Select(c => c.EmissionKey));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(firstCalls[0].Document), System.Text.Json.JsonSerializer.Serialize(retried[0].Document));
    }

    [Fact]
    public async Task RetryGuides_Rechazada_SeReplanificaConDatosCorregidos()
    {
        var (env, id) = await TwoGuidesAsync();
        env.Dte.RejectNext = "Error de esquema en CmnaDest";
        await env.Svc.DispatchAsync(id);
        var guides = await GuidesOf(env, id);
        Assert.Equal(TransportGuideStatuses.Failed, guides[0].Status);
        Assert.Equal("Error de esquema en CmnaDest", guides[0].LastError);

        env.AddLines(1, ("ART-CORREGIDO", 5, 1));
        var r = await env.Svc.RetryGuidesAsync(id);

        Assert.True(r.Success, string.Join(" | ", r.Errors));
        var resent = env.Dte.Calls.Last(c => c.EmissionKey == guides[0].EmissionKey);
        Assert.Equal("ART-CORREGIDO", resent.Document.Lines.Single().ItemCode);
    }

    [Fact]
    public async Task RetryGuides_SoloConGuiasPendientes()
    {
        var (env, id) = await TwoGuidesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.RetryGuidesAsync(id));
    }

    [Fact]
    public async Task Dispatch_DosVeces_NoDuplicaGuias()
    {
        var (env, id) = await TwoGuidesAsync();
        env.Dte.Down = true;
        await env.Svc.DispatchAsync(id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.DispatchAsync(id));
        Assert.Equal(2, (await GuidesOf(env, id)).Count);
        env.Dte.Down = false;
        await env.Svc.RetryGuidesAsync(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.RetryGuidesAsync(id)); // ya despachado
        Assert.Equal(2, (await GuidesOf(env, id)).Count);
        Assert.Equal(2, env.Dte.Calls.Select(c => c.EmissionKey).Distinct().Count());
    }

    [Fact]
    public async Task Annul_GuiasPendientesSinEmitidas_Abandona()
    {
        var (env, id) = await TwoGuidesAsync();
        env.Dte.Down = true;
        await env.Svc.DispatchAsync(id);

        await env.Svc.AnnulAsync(id, "Camión en panne");

        Assert.Equal(ManifestStatuses.Annulled, (await env.Svc.GetAsync(id))!.Status);
        Assert.All(await GuidesOf(env, id), g => Assert.Equal(TransportGuideStatuses.Abandoned, g.Status));
        Assert.Equal(2, env.Dte.Abandoned.Count);
    }

    [Fact]
    public async Task Annul_ConGuiaEmitida_Falla()
    {
        var (env, id) = await TwoGuidesAsync();
        await env.Svc.DispatchAsync(id);
        var guides = await GuidesOf(env, id);
        guides[1].Status = TransportGuideStatuses.Pending;
        var m = (await env.Svc.GetAsync(id))!;
        m.Status = ManifestStatuses.PendingGuides;
        await env.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.AnnulAsync(id, "x"));
        Assert.Contains("guías emitidas", ex.Message);
    }

    [Fact]
    public async Task Dispatch_GuiasDeshabilitadas_ComportamientoEtapa1()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.DraftWithDocsAsync(1);
        var r = await env.Svc.DispatchAsync(id);
        Assert.True(r.Success);
        Assert.Equal(ManifestStatuses.Dispatched, (await env.Svc.GetAsync(id))!.Status);
        Assert.Empty(env.Dte.Calls);
        Assert.False((await env.Guides.PreviewAsync(id)).Enabled);
    }

    [Fact]
    public async Task GetPdf_Todas_UneLosPdf()
    {
        var (env, id) = await TwoGuidesAsync();
        await env.Svc.DispatchAsync(id);

        var pdf = await env.Guides.GetPdfAsync(id, null);

        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.Equal(2, doc.PageCount);
    }

    [Fact]
    public async Task GetPdf_OtraCompania_NoExiste()
    {
        var (env, id) = await TwoGuidesAsync();
        await env.Svc.DispatchAsync(id);
        env.Company.CompanyId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Guides.GetPdfAsync(id, null));
    }

    [Fact]
    public async Task SetDocumentCommune_PermitidoConGuiasPendientes()
    {
        var (env, id) = await TwoGuidesAsync();
        env.Dte.Down = true;
        await env.Svc.DispatchAsync(id);
        var doc = (await env.Svc.GetAsync(id))!.Documents.First();
        await env.Svc.SetDocumentCommuneAsync(id, doc.Id, env.CuricoId);
        Assert.Equal(env.CuricoId, (await env.Svc.GetAsync(id))!.Documents.First(d => d.Id == doc.Id).DestinationCommuneId);
    }

    [Fact]
    public async Task RetryGuides_GuiaDesincronizada_SeSincronizaSinReplanificar()
    {
        var (env, id) = await TwoGuidesAsync();
        env.Dte.Down = true;
        await env.Svc.DispatchAsync(id);
        var guides = await GuidesOf(env, id);
        // La pasarela emitió la guía 1 en segundo plano, pero en Despachos quedó "planificada" (caída a mitad).
        env.Dte.Emissions[guides[0].EmissionKey] = new DteEmissionResult(guides[0].EmissionKey, DteEmissionStatuses.Issued, 900, null, "FAKE", "certification");
        guides[0].Status = TransportGuideStatuses.Planned;
        await env.Db.SaveChangesAsync();
        env.AddLines(1, ("ART-CAMBIADO", 9, 1)); // SAP cambió: replanificar daría otro contenido
        env.Dte.Down = false;

        var r = await env.Svc.RetryGuidesAsync(id);

        Assert.True(r.Success, string.Join(" | ", r.Errors));
        var after = await GuidesOf(env, id);
        Assert.Equal(TransportGuideStatuses.Issued, after[0].Status);
        Assert.Equal(900, after[0].Folio);
        Assert.DoesNotContain("ART-CAMBIADO", after[0].DocumentJson);
    }

    [Fact]
    public async Task RetryGuides_TodasAbandonadas_NoDespacha()
    {
        var (env, id) = await TwoGuidesAsync();
        env.Dte.Down = true;
        await env.Svc.DispatchAsync(id);
        foreach (var g in await GuidesOf(env, id)) g.Status = TransportGuideStatuses.Abandoned;
        await env.Db.SaveChangesAsync();
        env.Dte.Down = false;

        var r = await env.Svc.RetryGuidesAsync(id);

        Assert.False(r.Success);
        Assert.Equal(ManifestStatuses.PendingGuides, (await env.Svc.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Annul_GuiaPlanificadaConEmisionEnPasarela_LaAbandona()
    {
        var (env, id) = await TwoGuidesAsync();
        env.Dte.Down = true;
        await env.Svc.DispatchAsync(id);
        var guides = await GuidesOf(env, id);
        guides[0].Status = TransportGuideStatuses.Planned; // desincronizada: la pasarela sí tiene la emisión
        await env.Db.SaveChangesAsync();

        await env.Svc.AnnulAsync(id, "Camión en panne");

        Assert.Contains(guides[0].EmissionKey, env.Dte.Abandoned);
        Assert.Contains(guides[1].EmissionKey, env.Dte.Abandoned);
    }

    [Fact]
    public async Task RetryGuides_GuiasDeshabilitadasDespues_SigueFuncionando()
    {
        var (env, id) = await TwoGuidesAsync();
        env.Dte.Down = true;
        await env.Svc.DispatchAsync(id);
        var s = env.Db.Settings.Single(x => x.CompanyId == env.Company.CompanyId);
        s.GuidesEnabled = false;
        await env.Db.SaveChangesAsync();
        env.Dte.Down = false;

        var r = await env.Svc.RetryGuidesAsync(id);

        Assert.True(r.Success, string.Join(" | ", r.Errors));
        Assert.Equal(ManifestStatuses.Dispatched, (await env.Svc.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Dispatch_Concurrente_ElSegundoFallaSinPisarLaSalida()
    {
        var name = "despachos-" + Guid.NewGuid();
        var env = ManifestTestEnv.Build(db: TestDb.Create(name));
        await env.EnableGuidesAsync();
        env.AddSap(1);
        var id = await env.GuideReadyDraftAsync(1);

        // Segundo usuario: otro contexto que cargó el borrador ANTES de que el primero despachara.
        var db2 = TestDb.Create(name);
        var master2 = new MasterDataService(db2, env.Company, env.User);
        var closure2 = new MonthlyClosureService(db2, env.Company, env.User, env.Clock);
        var cost2 = new ManifestCostService(db2, env.Company, env.Tariff, Modulo.Despachos.Tests.Dominio.Tarifas.TarifasTestData.Engine(), env.Clock, closure2);
        var guides2 = new TransportGuideService(db2, env.Company, env.User, env.Sap, env.Dte, env.Clock, env.Notifications);
        var svc2 = new ManifestService(db2, env.Company, env.User, env.Sap, master2, cost2, env.Clock, env.Notifications, closure2, guides: guides2);
        Assert.Equal(ManifestStatuses.Draft, (await svc2.GetAsync(id))!.Status);

        Assert.True((await env.Svc.DispatchAsync(id)).Success);
        var departed = (await env.Svc.GetAsync(id))!.DepartedAt;

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc2.DispatchAsync(id));
        var fresh = TestDb.Create(name);
        var m = fresh.Manifests.Single(x => x.Id == id);
        Assert.Equal(ManifestStatuses.Dispatched, m.Status);
        Assert.Equal(departed, m.DepartedAt);
        Assert.Single(fresh.TransportGuides.Where(g => g.ManifestId == id));
    }
}
