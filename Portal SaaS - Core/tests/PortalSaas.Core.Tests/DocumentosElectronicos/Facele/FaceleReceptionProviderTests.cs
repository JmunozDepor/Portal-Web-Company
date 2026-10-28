using System.Net;
using System.Text;
using System.Xml.Linq;
using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Core.DocumentosElectronicos;
using PortalSaas.Core.DocumentosElectronicos.Proveedores.Facele;

namespace PortalSaas.Core.Tests.DocumentosElectronicos.Facele;

public class FaceleReceptionProviderTests
{
    private static readonly DteReceptionContext Ctx =
        new("https://caoba.docele.cl/cl-dol", "usuario-fixture", "clave-fixture-NO-real", "76030680-0", "facturas@depor.cl", "certification");

    /// <summary>Responde los fixtures en orden (el último se repite) y guarda cada request.</summary>
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly string[] _fixtures;
        private int _next;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        public SequenceHandler(params string[] fixtures) => _fixtures = fixtures;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            var fixture = _fixtures[Math.Min(_next++, _fixtures.Length - 1)];
            var content = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "DocumentosElectronicos", "Facele", "Fixtures", fixture), ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "text/xml") };
        }
    }

    private static (FaceleReceptionProvider Provider, SequenceHandler Handler) Build(params string[] fixtures)
    {
        var handler = new SequenceHandler(fixtures);
        return (new FaceleReceptionProvider(new FaceleSoapClient(new HttpClient(handler))), handler);
    }

    private static XElement Op(string body) =>
        XDocument.Parse(body).Descendants().First(e => e.Parent?.Name.LocalName == "Body");

    private static string? Val(XElement op, string name) => op.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    [Fact]
    public async Task ListReceived_MapeaRegistro()
    {
        var (p, h) = Build("consultar-recepcion-ok.resp.xml");

        var list = await p.ListReceivedAsync(Ctx, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 27), CancellationToken.None);

        var r = list.First();
        Assert.Equal("76412553-1", r.IssuerTaxId);
        Assert.Equal("Comercial Textil Andes SpA", r.IssuerName);
        Assert.Equal(33, r.DteType);
        Assert.Equal(18452, r.Folio);
        Assert.Equal(new DateOnly(2026, 10, 23), r.IssueDate);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 12, 10, 0, TimeSpan.Zero), r.SiiReceivedAt);
        Assert.Equal(TimeSpan.Zero, r.SiiReceivedAt!.Value.Offset);
        Assert.Equal(2000000, r.Net);
        Assert.Equal(380000, r.Vat);
        Assert.Equal(2380000, r.Total);
        Assert.Equal("Por_Aprobar", r.CommercialStatus);
        Assert.Equal("Por_Aceptar", r.ClaimStatus);
        Assert.Equal("SII_Documento_OK", r.SiiStatus);
        Assert.Equal("https://caoba.docele.cl/pdf/x1", r.Url);

        Assert.Null(r.RespondedAction); // Por_Aceptar: sin respuesta todavía
        var second = list[1];
        Assert.Equal(DteResponseActions.AcceptContent, second.RespondedAction);
        Assert.True(second.IsAssigned);
        Assert.Equal(4100000, second.Exempt);
        Assert.Contains("\"27\"", second.OtherTaxesJson);
        Assert.NotNull(second.SiiReceivedAt); // sin zona horaria: se interpreta como hora de Chile

        Assert.Equal("https://caoba.docele.cl/cl-dol/DTEService", h.Requests[0].RequestUri!.ToString());
        var op = Op(h.Bodies[0]);
        Assert.Equal("consultar", op.Name.LocalName);
        Assert.Equal("RECEPCION", Val(op, "operacion"));
        Assert.Equal("76030680-0", Val(op, "rutAbonado"));
        Assert.Equal("facturas@depor.cl", Val(op, "userMail"));
        Assert.Equal("2026-10-01", Val(op, "fechaEmision"));
        Assert.Equal("2026-10-27", Val(op, "fechaEmisionHasta"));
        Assert.Equal("usuario-fixture", h.Requests[0].Headers.GetValues("facele.user").Single());
    }

    /// <summary>
    /// Estructura real de caoba (2026-10-28): sin estadoSII, url ni anulado; las facturas al contado vienen con
    /// estadoLey20956 = Documento_Pagado_Al_Contado y las notas de crédito con No_Aplica. El SII no admite aceptar
    /// ni reclamar esas: quedan marcadas como no respondibles y sin respuesta registrada.
    /// </summary>
    [Fact]
    public async Task ListReceived_EstructuraCaoba_ContadoYNoAplica_NoRespondibles()
    {
        var (p, _) = Build("consultar-recepcion-caoba.resp.xml");

        var list = await p.ListReceivedAsync(Ctx, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), CancellationToken.None);

        Assert.Equal(3, list.Count);
        var credit = list[0];
        Assert.Null(credit.NotRespondableReason);
        Assert.Null(credit.RespondedAction);
        Assert.Null(credit.SiiStatus);
        Assert.Null(credit.Url);
        Assert.Equal(1049819, credit.Total);

        var cash = list[1];
        Assert.Equal(34, cash.DteType);
        Assert.Equal("Pagada al contado: el SII no admite aceptarla ni reclamarla.", cash.NotRespondableReason);
        Assert.Null(cash.RespondedAction);

        var creditNote = list[2];
        Assert.Equal("IMPRESOS ROMÁN SPA", creditNote.IssuerName);
        Assert.Equal("El SII no admite aceptar ni reclamar este documento.", creditNote.NotRespondableReason);
        Assert.Null(creditNote.RespondedAction);
    }

    [Fact]
    public async Task ListReceived_Pagina_HastaQueVieneMenosQueLimit()
    {
        var (p, h) = Build("consultar-recepcion-ok.resp.xml", "consultar-recepcion-pagina2.resp.xml");
        p.PageSize = 2;

        var list = await p.ListReceivedAsync(Ctx, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 27), CancellationToken.None);

        Assert.Equal(3, list.Count);
        Assert.Equal(2, h.Requests.Count);
        Assert.Equal("0", Val(Op(h.Bodies[0]), "offset"));
        Assert.Equal("2", Val(Op(h.Bodies[1]), "offset"));
        Assert.Equal("2", Val(Op(h.Bodies[1]), "limit"));
    }

    [Fact]
    public async Task GetReferences_Devuelve801()
    {
        var (p, h) = Build("consultar-referencia-801.resp.xml");

        var refs = await p.GetReferencesAsync(Ctx, "76412553-1", 33, 18452, CancellationToken.None);

        var oc = refs.First();
        Assert.Equal("801", oc.Type);
        Assert.Equal("4512", oc.Folio);
        Assert.Equal(new DateOnly(2026, 10, 12), oc.Date);
        Assert.Equal("ORDEN DE COMPRA", oc.Reason);
        Assert.Equal(2, refs.Count);
        var op = Op(h.Bodies[0]);
        Assert.Equal("consultarReferencia", op.Name.LocalName);
        Assert.Equal("RECEPCION", Val(op, "operacion"));
        Assert.Equal("76412553-1", Val(op, "rutEmisor"));
        Assert.Equal("18452", Val(op, "folio"));
    }

    [Theory]
    [InlineData(DteResponseActions.AcceptContent, "Acepta_Contenido_Documento")]
    [InlineData(DteResponseActions.ClaimContent, "Reclamo_Contenido_Documento")]
    [InlineData(DteResponseActions.ClaimPartialGoods, "Reclamo_Falta_Parcial_Mercaderias")]
    [InlineData(DteResponseActions.ClaimTotalGoods, "Reclamo_Falta_Total_Mercaderias")]
    [InlineData(DteResponseActions.GoodsReceipt, "Otorga_Recibo_Mercaderias")]
    public async Task Respond_MapeaAcciones(string action, string faceleAction)
    {
        var (p, h) = Build("aprobar-ok.resp.xml");

        var r = await p.RespondAsync(Ctx, "76412553-1", 33, 18452, action, CancellationToken.None);

        Assert.True(r.Ok);
        var op = Op(h.Bodies[0]);
        Assert.Equal("aprobarDTERecibidos", op.Name.LocalName);
        Assert.Equal("76030680-0", Val(op, "rutAbonado"));
        var aprobacion = op.Elements().Single(e => e.Name.LocalName == "aprobacion");
        Assert.Equal("76412553-1", Val(aprobacion, "rutEmisor"));
        Assert.Equal("33", Val(aprobacion, "tipoDTE"));
        Assert.Equal("18452", Val(aprobacion, "folio"));
        Assert.Equal(faceleAction, Val(aprobacion, "accion"));
    }

    [Fact]
    public async Task Respond_EstadoError_DevuelveErrorConGlosa()
    {
        var (p, _) = Build("aprobar-error.resp.xml");

        var r = await p.RespondAsync(Ctx, "76412553-1", 33, 18452, DteResponseActions.ClaimContent, CancellationToken.None);

        Assert.False(r.Ok);
        Assert.False(r.AlreadyResponded);
        Assert.Contains("no existe", r.Error);
    }

    [Fact]
    public async Task Respond_GlosaYaRespondido_AlreadyResponded()
    {
        var (p, _) = Build("aprobar-ya-respondido.resp.xml");

        var r = await p.RespondAsync(Ctx, "76412553-1", 33, 18452, DteResponseActions.AcceptContent, CancellationToken.None);

        Assert.False(r.Ok);
        Assert.True(r.AlreadyResponded);
    }

    [Fact]
    public async Task Respond_AccionDesconocida_Lanza()
    {
        var (p, _) = Build("aprobar-ok.resp.xml");
        await Assert.ThrowsAsync<ArgumentException>(() => p.RespondAsync(Ctx, "76412553-1", 33, 18452, "otra", CancellationToken.None));
    }

    [Fact]
    public void Capabilities_Facele()
    {
        var (p, _) = Build("aprobar-ok.resp.xml");
        Assert.Equal("FACELE", p.Code);
        Assert.Equal(new[] { 33, 34, 43 }, p.Capabilities.RespondableTypes);
        Assert.Equal(DteResponseActions.All.Count, p.Capabilities.SupportedActions.Count);
        Assert.False(p.Capabilities.SupportsDocumentDownload);
    }

    [Fact]
    public async Task Test_UsaConsultarResumen()
    {
        var (p, h) = Build("resumen-ok.resp.xml");
        await p.TestAsync(Ctx, CancellationToken.None);
        Assert.Equal("consultarResumen", Op(h.Bodies[0]).Name.LocalName);
    }
}
