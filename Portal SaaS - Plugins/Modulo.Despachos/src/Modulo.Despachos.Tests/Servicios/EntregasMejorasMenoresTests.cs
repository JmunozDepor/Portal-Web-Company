using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modulo.Despachos.Data;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Modelos;
using AgruparTraslados = Modulo.Despachos.Pages.Despachos.AgruparTraslados;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Mejoras menores de la revisión final de Entregas con guía previa.</summary>
public class EntregasMejorasMenoresTests
{
    /// <summary>Hoja EG-000001 (701 y 702, con líneas valorizadas) escaneada en un borrador con fecha de llegada.</summary>
    private static async Task<(ManifestTestEnv Env, Guid Id)> DeliveriesDraftAsync(ManifestTestEnv? env = null, params int[] entries)
    {
        env ??= DeliveryGroupServiceTests.Env();
        if (entries.Length == 0) entries = new[] { 701, 702 };
        Assert.Single(await env.Deliveries.GenerateAsync(DeliveryGroupServiceTests.Input(entries)));
        foreach (var e in entries)
            env.Sap.Lines[e] = new SapDocumentLines("Grandes tiendas", "Santiago",
                new[]
                {
                    new SapDocumentLine(0, "SKU" + e, "Zapatilla " + e, 2, "UN", 1m, Amount: 20000m),
                    new SapDocumentLine(1, "SKV" + e, "Polera " + e, 1, "UN", 1m, Amount: 5000m),
                });
        var id = await env.Svc.CreateDraftAsync(env.Header(arrival: ManifestTestEnv.Fecha));
        Assert.Equal(AddDocumentOutcomes.GroupAdded, (await env.Svc.AddDocumentAsync(id, new DocumentCapture("EG-000001", null, null))).Outcome);
        return (env, id);
    }

    // 1. Re-chequeo en SAP al despachar.
    [Fact]
    public async Task Despachar_EntregaFacturadaDespuesDelEscaneo_BloqueaConMensaje()
    {
        var (env, id) = await DeliveriesDraftAsync();
        var i = env.Sap.Deliveries.FindIndex(x => x.DocEntry == 702);
        env.Sap.Deliveries[i] = env.Sap.Deliveries[i] with { IsOpen = false };

        var r = await env.Svc.DispatchAsync(id);

        Assert.False(r.Success);
        Assert.Contains("La Entrega 1702 ya no está abierta en SAP (facturada, cerrada o anulada); quítela del manifiesto.", r.Errors);
        Assert.Empty(env.Dte.Calls);
    }

    // 2. Entrega anulada: la fila de cancelación (CANCELED = 'C') no cuenta.
    [Fact]
    public void SeedPorLpn_ExcluyeDocumentosDeCancelacion() =>
        Assert.Contains("T0.\"CANCELED\" <> 'C'", SapQuerySeeds.Get(SapQueryKinds.DeliveryByLpn));

    // 3. Líneas sin pedido: motivo propio.
    [Fact]
    public async Task Candidatos_LineasSinPedido_MotivoPropio()
    {
        var env = DeliveryGroupServiceTests.Env();
        env.Sap.Deliveries.Add(new SapDeliveryInfo(706, 1706, new DateOnly(2026, 11, 9), "C001", "Falabella", "77261280-K",
            "Av. Kennedy 9001", "Rancagua", "LPN706", "4500000001", new DateOnly(2026, 11, 5), 1, 4, 40000, true, LinesWithoutPo: 1));

        var c = await env.Deliveries.ListCandidatesAsync("C001", DeliveryGroupServiceTests.Desde);

        Assert.Equal("La Entrega tiene líneas sin OC (sin pedido o pedido sin N° de referencia); sepárela en SAP.",
            c.Blocked.Single(b => b.Delivery.DocEntry == 706).Reason);
    }

    [Theory]
    [InlineData(SapQueryKinds.DeliveryPending)]
    [InlineData(SapQueryKinds.DeliveryByLpn)]
    public void Seeds_TraenLineasSinPedido(string kind) => Assert.Contains("\"no_po_lines\"", SapQuerySeeds.Get(kind));

    // 4. Rótulos: el PDF del manifiesto no llama "Factura" a una Entrega ni a una transferencia.
    [Theory]
    [InlineData(DocumentKinds.Delivery, "Entrega")]
    [InlineData(DocumentKinds.Transfer, "Transferencia")]
    [InlineData(DocumentKinds.Invoice, "Factura")]
    [InlineData(DocumentKinds.TransferGuide, "Guía")]
    public void PdfManifiesto_RotuloPorTipo(string kind, string label)
    {
        var m = new Manifest { Number = 5 };
        m.Documents.Add(new ManifestDocument { Folio = 9, DocumentKind = kind, DestinationCommuneName = "Talca", Units = 1, Packages = 1 });
        var model = ManifestPdfService.BuildModel(m, new Dictionary<long, string>());
        Assert.Equal(label, model.Groups[0].Rows[0].Kind);
    }

    // 5. La hoja EG- no se imprime desde Agrupar traslados.
    [Fact]
    public async Task AgruparTraslados_PdfDeHojaEG_NoEncontrada()
    {
        var env = DeliveryGroupServiceTests.Env();
        var g = Assert.Single(await env.Deliveries.GenerateAsync(DeliveryGroupServiceTests.Input(701)));
        env.User.Grant("Despachos.agrupar-traslados", PortalActions.View);
        var page = new AgruparTraslados.DetalleModel(env.Groups, new DispatchGroupPdfService(env.Groups, env.Company), env.User)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider()),
        };

        Assert.IsType<NotFoundResult>(await page.OnGetPdfAsync(g.Id));
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    // 6. Reintento después de apagar la emisión: la guía rechazada de factura se replanifica igual.
    [Fact]
    public async Task Reintento_EmisionApagadaDespues_ReplanificaGuiaDeFactura()
    {
        var env = ManifestTestEnv.Build();
        await env.EnableGuidesAsync();
        var id = await env.GuideReadyDraftAsync(1);
        env.Dte.RejectNext = "Error de esquema";
        await env.Svc.DispatchAsync(id);
        var key = (await env.Db.TransportGuides.AsNoTracking().SingleAsync(g => g.ManifestId == id)).EmissionKey;
        var s = env.Db.Settings.Single(x => x.CompanyId == env.Company.CompanyId);
        s.GuidesEnabled = false;
        await env.Db.SaveChangesAsync();
        env.AddLines(1, ("ART-CORREGIDO", 5, 1));

        var r = await env.Svc.RetryGuidesAsync(id);

        Assert.True(r.Success, string.Join(" | ", r.Errors));
        Assert.Equal("ART-CORREGIDO", env.Dte.Calls.Last(c => c.EmissionKey == key).Document.Lines.Single().ItemCode);
    }

    // 7. Entrega repartida en dos guías: queda escrito el folio de la primera (SAP guarda un solo folio).
    [Fact]
    public async Task EntregaEnDosGuias_EscribeElFolioDeLaPrimera()
    {
        var env = DeliveryGroupServiceTests.Env();
        var s = env.Db.Settings.Single(x => x.CompanyId == env.Company.CompanyId);
        s.GuideMaxLines = 1;
        await env.Db.SaveChangesAsync();
        var (_, id) = await DeliveriesDraftAsync(env, 701);
        env.Dte.NextFolio = 900;

        Assert.True((await env.Svc.DispatchAsync(id)).Success);

        Assert.Equal(2, env.Dte.Calls.Count);
        var w = Assert.Single(env.Writer.DeliveryWrites);
        Assert.Equal((701, 900L), (w.DocEntry, (long)w.Fields["FolioNumber"]!));
    }

    // 8. "Probar escritura" en una Entrega sin folio no envía valores nulos.
    [Fact]
    public async Task ProbarEscrituraEntrega_SinFolio_NoEnviaNulos()
    {
        var env = DeliveryGroupServiceTests.Env();
        env.Writer.DeliveryCurrent[701] = new Dictionary<string, object?> { ["FolioNumber"] = null, ["FolioPrefixString"] = null };

        await env.Guides.TestSapDeliveryWriteAsync(701);

        var w = Assert.Single(env.Writer.DeliveryWrites);
        Assert.Equal(new Dictionary<string, object?> { ["FolioPrefixString"] = "" }, w.Fields);
    }

    // 9. Sin servicio de guías, un manifiesto con Entregas no se despacha.
    [Fact]
    public async Task Despachar_Entregas_SinServicioDeGuias_Bloquea()
    {
        var env = ManifestTestEnv.Build(factory: (db, company, user, sap, master, cost, time, notifications, closure) =>
            new ManifestService(db, company, user, sap, master, cost, time, notifications, closure));
        DeliveryGroupServiceTests.AddDelivery(env, 701, "4500000001");
        var (_, id) = await DeliveriesDraftAsync(env, 701);

        var r = await env.Svc.DispatchAsync(id);

        Assert.False(r.Success);
        Assert.Contains("La emisión de guías no está disponible: las Entregas con guía previa no se pueden despachar sin guía.", r.Errors);
        Assert.Equal(ManifestStatuses.Draft, (await env.Svc.GetAsync(id))!.Status);
    }

    // 10. Dos agrupaciones simultáneas chocan en el correlativo: se reintenta con el número siguiente.
    private sealed class FailFirstGroupInsert : SaveChangesInterceptor
    {
        public int Remaining = 1;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Remaining > 0 && eventData.Context!.ChangeTracker.Entries<DispatchGroup>().Any(e => e.State == EntityState.Added))
            {
                Remaining--;
                throw new DbUpdateException("duplicate key value violates unique constraint \"uq_despachos_dispatch_groups_company_id_number\"");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static (ManifestTestEnv Env, FailFirstGroupInsert Interceptor) RacingEnv()
    {
        var interceptor = new FailFirstGroupInsert { Remaining = 0 };
        var db = new DespachosDbContext(new DbContextOptionsBuilder<DespachosDbContext>()
            .UseInMemoryDatabase("race-" + Guid.NewGuid()).AddInterceptors(interceptor).Options);
        return (ManifestTestEnv.Build(db: db), interceptor);
    }

    [Fact]
    public async Task AgruparEntregas_ChoqueDeCorrelativo_ReintentaYCrea()
    {
        var (env, interceptor) = RacingEnv();
        DeliveryGroupServiceTests.AddDelivery(env, 701, "4500000001");
        interceptor.Remaining = 1;

        var g = Assert.Single(await env.Deliveries.GenerateAsync(DeliveryGroupServiceTests.Input(701)));

        Assert.Equal(1, g.Number);
        Assert.Equal(1, await env.Db.DispatchGroups.CountAsync());
    }

    [Fact]
    public async Task AgruparTraslados_ChoqueDeCorrelativo_ReintentaYCrea()
    {
        var (env, interceptor) = RacingEnv();
        await env.Master.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = "TDA01", Name = "Tienda Uno", Address = "A 1", CommuneName = "Rancagua" });
        DispatchGroupServiceTests.AddTransfer(env, 501, "TDA01", "CTBO501", 5);
        interceptor.Remaining = 1;

        Assert.Single(await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, DispatchGroupServiceTests.Desde, new[] { 501 }));
        Assert.Equal(1, await env.Db.DispatchGroups.CountAsync());
    }

    [Fact]
    public async Task AgruparEntregas_ChoquePersistente_MensajeAmable()
    {
        var (env, interceptor) = RacingEnv();
        DeliveryGroupServiceTests.AddDelivery(env, 701, "4500000001");
        interceptor.Remaining = 10;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Deliveries.GenerateAsync(DeliveryGroupServiceTests.Input(701)));

        Assert.Equal("Otro usuario agrupó al mismo tiempo; vuelva a intentarlo.", ex.Message);
    }

    // 11. Manifiesto mixto (factura + Entregas) con la emisión Res. 154 apagada y encendida.
    [Fact]
    public async Task Mixto_EmisionApagada_SoloGuiaDeEntregas()
    {
        var env = DeliveryGroupServiceTests.Env();
        var (_, id) = await DeliveriesDraftAsync(env, 701);
        env.AddSap(1);
        env.AddLines(1, ("ART-1", 2, 1.5m));
        Assert.Equal("added", (await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 1))).Outcome);

        Assert.True((await env.Svc.DispatchAsync(id)).Success);

        var call = Assert.Single(env.Dte.Calls);
        Assert.Equal("801", call.Document.References.Single().DocumentType);
    }

    [Fact]
    public async Task Mixto_EmisionEncendida_GuiaDeFacturaYGuiaDeEntregas()
    {
        var env = DeliveryGroupServiceTests.Env();
        await env.EnableGuidesAsync();
        var (_, id) = await DeliveriesDraftAsync(env, 701);
        env.AddSap(1);
        env.AddLines(1, ("ART-1", 2, 1.5m));
        Assert.Equal("added", (await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 1))).Outcome);

        Assert.True((await env.Svc.DispatchAsync(id)).Success);

        Assert.Equal(2, env.Dte.Calls.Count);
        Assert.Equal(new[] { "33", "801" }, env.Dte.Calls.Select(c => c.Document.References.Single().DocumentType).OrderBy(x => x));
    }
}
