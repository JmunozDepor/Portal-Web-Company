using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Core.DocumentosElectronicos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.DocumentosElectronicos;

public class DteInboxServiceTests
{
    private sealed record Env(DteInboxService Inbox, PortalSaasDbContext Db, FakeReceptionProvider Provider, FakeCompany Company, FixedClock Clock);

    private static async Task<Env> BuildAsync()
    {
        var db = DteTestSupport.Db();
        var company = new FakeCompany();
        var provider = new FakeReceptionProvider();
        var registry = DteReceptionTestSupport.Registry(provider);
        var settings = new DteSettingsService(db, DteTestSupport.Crypto(), new DteProviderRegistry(new IDteProvider[] { new FakeDteProvider() }), registry);
        await settings.SaveAsync(company.CompanyId, DteTestSupport.ValidInput().WithReception());
        var clock = new FixedClock();
        var sync = new DteReceptionSyncService(db, settings, clock, NullLogger<DteReceptionSyncService>.Instance);
        return new Env(new DteInboxService(db, company, settings, registry, sync, clock), db, provider, company, clock);
    }

    private static async Task<DteReceived> AddAsync(Env env, int type = 33, long folio = 18452, Guid? companyId = null, string poRefsJson = "[]")
    {
        var doc = new DteReceived
        {
            CompanyId = companyId ?? env.Company.CompanyId, ProviderCode = "FAKE", Environment = "certification",
            IssuerTaxId = "76412553-1", IssuerName = "Comercial Textil Andes SpA", DteType = type, Folio = folio,
            IssueDate = new DateOnly(2026, 10, 23), SiiReceivedAt = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero),
            TotalAmount = 2380000, PurchaseOrderRefsJson = poRefsJson, UpdatedAt = env.Clock.Now,
        };
        env.Db.DteReceived.Add(doc);
        await env.Db.SaveChangesAsync();
        return doc;
    }

    private static DteRespondRequest Req(long id, string key = "fp:1:1", string action = DteResponseActions.AcceptContent) =>
        new(id, action, "motivo", "rule:oc-saldo", key);

    [Fact]
    public async Task Respond_Ok_QuedaSent_YNoSeLlamaDosVeces()
    {
        var env = await BuildAsync();
        var doc = await AddAsync(env);

        var first = await env.Inbox.RespondAsync(Req(doc.Id));
        var second = await env.Inbox.RespondAsync(Req(doc.Id));

        Assert.Equal(DteRespondOutcome.Sent, first.Outcome);
        Assert.Equal(DteRespondOutcome.AlreadyResponded, second.Outcome);
        Assert.Single(env.Provider.Responds);
        var row = await env.Db.DteReceived.SingleAsync();
        Assert.Equal(DteResponseStatuses.Sent, row.ResponseStatus);
        Assert.Equal("rule:oc-saldo", row.ResponseActor);
        Assert.Equal("fp:1:1", row.ResponseKey);
        Assert.Equal(env.Clock.Now, row.RespondedAt);
    }

    [Fact]
    public async Task Respond_FallaRed_QuedaFailed_YReintentoMismaClaveEnvia()
    {
        var env = await BuildAsync();
        var doc = await AddAsync(env);
        env.Provider.FailNextResponds = 1;

        var first = await env.Inbox.RespondAsync(Req(doc.Id));
        Assert.Equal(DteRespondOutcome.Failed, first.Outcome);
        Assert.Equal(DteResponseStatuses.Failed, (await env.Db.DteReceived.SingleAsync()).ResponseStatus);

        var retry = await env.Inbox.RespondAsync(Req(doc.Id));
        Assert.Equal(DteRespondOutcome.Sent, retry.Outcome);
        Assert.Equal(2, (await env.Db.DteReceived.SingleAsync()).ResponseAttempts);
    }

    [Fact]
    public async Task Respond_ExternalNoLlamaAlOperador()
    {
        var env = await BuildAsync();
        var doc = await AddAsync(env);
        doc.ResponseStatus = DteResponseStatuses.External;
        await env.Db.SaveChangesAsync();

        var r = await env.Inbox.RespondAsync(Req(doc.Id));

        Assert.Equal(DteRespondOutcome.AlreadyResponded, r.Outcome);
        Assert.Empty(env.Provider.Responds);
    }

    [Fact]
    public async Task Respond_PendienteConOtraClave_NoLlama()
    {
        var env = await BuildAsync();
        var doc = await AddAsync(env);
        doc.ResponseStatus = DteResponseStatuses.Pending;
        doc.ResponseKey = "otra";
        await env.Db.SaveChangesAsync();

        var r = await env.Inbox.RespondAsync(Req(doc.Id));

        Assert.Equal(DteRespondOutcome.AlreadyResponded, r.Outcome);
        Assert.Empty(env.Provider.Responds);
    }

    [Fact]
    public async Task Respond_TipoNoRespondible_NotRespondable()
    {
        var env = await BuildAsync();
        var doc = await AddAsync(env, type: 61);

        var r = await env.Inbox.RespondAsync(Req(doc.Id));

        Assert.Equal(DteRespondOutcome.NotRespondable, r.Outcome);
        Assert.Empty(env.Provider.Responds);
    }

    [Fact]
    public async Task List_Texto_IgnoraMayusculas()
    {
        var env = await BuildAsync();
        await AddAsync(env);

        var byName = await env.Inbox.ListAsync(new DteReceivedFilter(null, null, "textil andes", 50));

        Assert.Single(byName);
    }

    [Fact]
    public async Task Respond_DocumentoNoRespondible_NotRespondableConMotivo_YNoQuedaPendiente()
    {
        var env = await BuildAsync();
        var doc = await AddAsync(env, type: 34);
        doc.NotRespondableReason = "Pagada al contado: el SII no admite aceptarla ni reclamarla.";
        await env.Db.SaveChangesAsync();

        var r = await env.Inbox.RespondAsync(Req(doc.Id, action: DteResponseActions.ClaimContent));

        Assert.Equal(DteRespondOutcome.NotRespondable, r.Outcome);
        Assert.Equal("Pagada al contado: el SII no admite aceptarla ni reclamarla.", r.Error);
        Assert.Empty(env.Provider.Responds);
        Assert.Equal(DteResponseStatuses.None, (await env.Db.DteReceived.SingleAsync()).ResponseStatus);
        Assert.Equal("Pagada al contado: el SII no admite aceptarla ni reclamarla.", (await env.Inbox.GetAsync(doc.Id))!.NotRespondableReason);
    }

    [Fact]
    public async Task Respond_OperadorDiceYaRespondida_PendingConfirmation()
    {
        var env = await BuildAsync();
        var doc = await AddAsync(env);
        env.Provider.NextRespond = new DteRespondProviderResult(false, true, "El documento ya fue aceptado");

        var r = await env.Inbox.RespondAsync(Req(doc.Id));

        Assert.Equal(DteRespondOutcome.PendingConfirmation, r.Outcome);
        var row = await env.Db.DteReceived.SingleAsync();
        Assert.Equal(DteResponseStatuses.Pending, row.ResponseStatus);
        Assert.Contains("próxima sincronización", row.LastError);
    }

    [Fact]
    public async Task Respond_RechazoDeNegocio_Failed()
    {
        var env = await BuildAsync();
        var doc = await AddAsync(env);
        env.Provider.NextRespond = new DteRespondProviderResult(false, false, "Folio no existe");

        var r = await env.Inbox.RespondAsync(Req(doc.Id));

        Assert.Equal(DteRespondOutcome.Failed, r.Outcome);
        Assert.Equal("Folio no existe", r.Error);
    }

    [Fact]
    public async Task Respond_OtraCompania_NoEncuentra()
    {
        var env = await BuildAsync();
        var doc = await AddAsync(env, companyId: Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Inbox.RespondAsync(Req(doc.Id)));
        Assert.Null(await env.Inbox.GetAsync(doc.Id));
    }

    [Fact]
    public async Task SetPurchaseOrderRefs_ConservaLasDelDte()
    {
        var env = await BuildAsync();
        var dteRef = new DtePurchaseOrderRef(4512, DtePurchaseOrderRefSources.Dte, null, env.Clock.Now);
        var doc = await AddAsync(env, poRefsJson: DteReceivedJson.PurchaseOrderRefs(new[] { dteRef }));
        var user = Guid.NewGuid();

        await env.Inbox.SetPurchaseOrderRefsAsync(doc.Id, new[] { 4530, 4531 }, user);
        await env.Inbox.SetPurchaseOrderRefsAsync(doc.Id, new[] { 4531 }, user);

        var refs = (await env.Inbox.GetAsync(doc.Id))!.PurchaseOrderRefs;
        Assert.Equal(new[] { 4512, 4531 }, refs.Select(r => r.DocNum).OrderBy(n => n));
        Assert.Equal(user, refs.Single(r => r.DocNum == 4531).UserId);
        Assert.Equal(DtePurchaseOrderRefSources.Manual, refs.Single(r => r.DocNum == 4531).Source);
    }

    [Fact]
    public async Task ListChangedSince_OrdenAscendente_YRespetaMax()
    {
        var env = await BuildAsync();
        var a = await AddAsync(env, folio: 1);
        env.Clock.Now = env.Clock.Now.AddMinutes(1);
        var b = await AddAsync(env, folio: 2);
        env.Clock.Now = env.Clock.Now.AddMinutes(1);
        await AddAsync(env, folio: 3);

        var page = await env.Inbox.ListChangedSinceAsync(DateTimeOffset.MinValue, 0, 2);

        Assert.Equal(new[] { a.Id, b.Id }, page.Select(d => d.Id));
    }

    [Fact]
    public async Task ListChangedSince_MismoUpdatedAt_NoSaltaFilas()
    {
        var env = await BuildAsync();
        var docs = new List<DteReceived>();
        for (var i = 1; i <= 3; i++) docs.Add(await AddAsync(env, folio: i)); // misma UpdatedAt

        var first = await env.Inbox.ListChangedSinceAsync(DateTimeOffset.MinValue, 0, 2);
        var last = first[^1];
        var second = await env.Inbox.ListChangedSinceAsync(last.UpdatedAt, last.Id, 2);

        Assert.Equal(docs.Select(d => d.Id), first.Concat(second).Select(d => d.Id));
    }

    [Fact]
    public async Task GetStatus_ExponeCapacidadesYHistorial()
    {
        var env = await BuildAsync();

        var status = await env.Inbox.GetStatusAsync();

        Assert.True(status.ReceptionEnabled);
        Assert.Equal("FAKE", status.ProviderCode);
        Assert.Contains(33, status.RespondableTypes);
        Assert.Equal(60, status.InitialHistoryDays);
        Assert.Equal(30, status.IntervalMinutes);
    }

    [Fact]
    public async Task Get_ExponeFormaDePagoVencimientoYSiHayPdf()
    {
        var env = await BuildAsync();
        var withLink = await AddAsync(env);
        withLink.PaymentForm = "CREDITO";
        withLink.DueDate = new DateOnly(2026, 11, 22);
        withLink.ProviderUrl = "https://alerce.docele.cl/pdf/1";
        var withoutLink = await AddAsync(env, folio: 99);
        await env.Db.SaveChangesAsync();

        var dto = await env.Inbox.GetAsync(withLink.Id);
        Assert.Equal("CREDITO", dto!.PaymentForm);
        Assert.Equal(new DateOnly(2026, 11, 22), dto.DueDate);
        Assert.True(dto.HasPdf);
        Assert.False((await env.Inbox.GetAsync(withoutLink.Id))!.HasPdf);
    }

    /// <summary>FACELE no entrega el XML, pero sí el enlace al PDF: se pide igual.</summary>
    [Fact]
    public async Task GetPdf_ConEnlace_LoPideAunqueElOperadorNoEntregueXml()
    {
        var env = await BuildAsync();
        env.Provider.Pdf = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var doc = await AddAsync(env);
        doc.ProviderUrl = "https://alerce.docele.cl/pdf/1";
        await env.Db.SaveChangesAsync();

        Assert.Equal(env.Provider.Pdf, await env.Inbox.GetPdfAsync(doc.Id));
        Assert.Equal("https://alerce.docele.cl/pdf/1", Assert.Single(env.Provider.PdfUrls));
    }

    [Fact]
    public async Task GetPdf_SinEnlaceNiDescarga_NullSinLlamar()
    {
        var env = await BuildAsync();
        var doc = await AddAsync(env);

        Assert.Null(await env.Inbox.GetPdfAsync(doc.Id));
        Assert.Empty(env.Provider.PdfUrls);
    }

    /// <summary>Operador caído: el módulo recibe null (no conoce las excepciones de la plataforma).</summary>
    [Fact]
    public async Task GetPdf_OperadorCaido_Null()
    {
        var env = await BuildAsync();
        env.Provider.ThrowOnPdf = true;
        var doc = await AddAsync(env);
        doc.ProviderUrl = "https://alerce.docele.cl/pdf/1";
        await env.Db.SaveChangesAsync();

        Assert.Null(await env.Inbox.GetPdfAsync(doc.Id));
    }

    /// <summary>
    /// Sin XML archivado, se pide al operador (FACELE: DocumentosRecibidosService en el servidor de emisión), se archiva y
    /// se lee. La segunda vez sale del archivo.
    /// </summary>
    [Fact]
    public async Task GetDocument_SinXmlArchivado_LoPideAlOperadorYLoArchiva()
    {
        var env = await BuildAsync();
        env.Provider.Capabilities = env.Provider.Capabilities with { SupportsDocumentDownload = true };
        var doc = await AddAsync(env);
        env.Provider.Xmls[(doc.IssuerTaxId, doc.DteType, doc.Folio)] =
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "DocumentosElectronicos", "Fixtures", "factura33-recibida.xml"));

        var first = await env.Inbox.GetDocumentAsync(doc.Id);
        var second = await env.Inbox.GetDocumentAsync(doc.Id);

        Assert.Equal(2, first!.Lines.Count);
        Assert.Equal(2380000, second!.Total);
        Assert.Equal(1, env.Provider.XmlCalls);
        Assert.NotNull((await env.Db.DteReceived.AsNoTracking().SingleAsync(x => x.Id == doc.Id)).Xml);
        Assert.Equal("https://caoba.docele.cl/cl-dol-auth", env.Provider.Contexts.Last().DocumentsBaseUrl);
    }

    [Fact]
    public async Task GetDocument_OperadorSinXml_Null()
    {
        var env = await BuildAsync();
        env.Provider.Capabilities = env.Provider.Capabilities with { SupportsDocumentDownload = true };
        var doc = await AddAsync(env);

        Assert.Null(await env.Inbox.GetDocumentAsync(doc.Id));
    }

    [Fact]
    public async Task GetDocument_OperadorSinDescarga_NoLoPide()
    {
        var env = await BuildAsync();
        var doc = await AddAsync(env);

        Assert.Null(await env.Inbox.GetDocumentAsync(doc.Id));
        Assert.Equal(0, env.Provider.XmlCalls);
    }

    [Fact]
    public async Task GetLines_LeeDelXmlArchivado()
    {
        var env = await BuildAsync();
        var doc = await AddAsync(env);
        doc.Xml = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "DocumentosElectronicos", "Fixtures", "factura33-recibida.xml"));
        await env.Db.SaveChangesAsync();

        var lines = await env.Inbox.GetLinesAsync(doc.Id);

        Assert.Equal(2, lines.Count);
        Assert.Empty(await env.Inbox.GetLinesAsync((await AddAsync(env, folio: 99)).Id));
    }
}
