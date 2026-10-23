using System.Net;
using System.Text;
using System.Xml.Linq;
using PortalSaas.Core.DocumentosElectronicos;
using PortalSaas.Core.DocumentosElectronicos.Proveedores.Facele;

namespace PortalSaas.Core.Tests.DocumentosElectronicos.Facele;

public class FaceleDteProviderTests
{
    private const string User = "usuario-fixture";
    private const string Pass = "clave-fixture-NO-real";
    private static readonly DteProviderContext Ctx = new("https://caoba.docele.cl/cl-dol-auth", User, Pass, "76030680-0", "certification");

    /// <summary>Handler que responde un fixture (o un código HTTP) y guarda el último request.</summary>
    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly string? _fixture;
        private readonly HttpStatusCode _status;
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        public FixtureHandler(string? fixture, HttpStatusCode status = HttpStatusCode.OK)
        {
            _fixture = fixture;
            _status = status;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var content = _fixture is null
                ? "Autenticacion ha fallado usuario-fixture clave-fixture-NO-real"
                : await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "DocumentosElectronicos", "Facele", "Fixtures", _fixture), ct);
            return new HttpResponseMessage(_status) { Content = new StringContent(content, Encoding.UTF8, "text/xml") };
        }
    }

    private static (FaceleDteProvider Provider, FixtureHandler Handler) Build(string? fixture, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FixtureHandler(fixture, status);
        return (new FaceleDteProvider(new FaceleSoapClient(new HttpClient(handler))), handler);
    }

    private static XElement BodyOp(FixtureHandler h) =>
        XDocument.Parse(h.Body!).Descendants().First(e => e.Parent?.Name.LocalName == "Body");

    [Fact]
    public async Task Reserve_Ok_DevuelveFolio()
    {
        var (p, h) = Build("genera-ok.resp.xml");
        var key = Guid.NewGuid();

        var r = await p.ReserveAsync(Ctx, "<DTE/>", 52, key, CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Equal(103, r.Folio);
        Assert.Equal("https://caoba.docele.cl/cl-dol-auth/DoceleOLService", h.Request!.RequestUri!.ToString());
        Assert.Equal(User, h.Request.Headers.GetValues("facele.user").Single());
        Assert.Equal(Pass, h.Request.Headers.GetValues("facele.pass").Single());
        var op = BodyOp(h);
        Assert.Equal("generaDTE", op.Name.LocalName);
        Assert.Equal(key.ToString(), op.Element("uuid")!.Value);
        Assert.Equal("XML", op.Element("formato")!.Value);
        Assert.Equal("<DTE/>", op.Element("xml")!.Value);
        Assert.Equal("76030680-0", op.Element("rutEmisor")!.Value);
    }

    [Fact]
    public async Task Reserve_UuidYaUtilizado_DevuelveFolioOriginal()
    {
        var (p, _) = Build("genera-uuid-usado.resp.xml");
        var r = await p.ReserveAsync(Ctx, "<DTE/>", 52, Guid.NewGuid(), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.True(r.UuidAlreadyUsed);
        Assert.Equal(103, r.Folio);
    }

    [Fact]
    public async Task Reserve_ErrorEsquema_DevuelveGlosa()
    {
        var (p, _) = Build("genera-error-esquema.resp.xml");
        var r = await p.ReserveAsync(Ctx, "<DTE/>", 52, Guid.NewGuid(), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.False(r.UuidAlreadyUsed);
        Assert.Null(r.Folio);
        Assert.Contains("TagInexistente", r.Error);
    }

    [Fact]
    public async Task Confirm_DevuelveXmlTimbrado()
    {
        var (p, h) = Build("obtiene-xml.resp.xml");
        var r = await p.ConfirmAsync(Ctx, 52, 103, CancellationToken.None);
        Assert.True(r.Ok);
        Assert.Contains("<TED", r.SignedXml);
        var op = BodyOp(h);
        Assert.Equal("obtieneDTE", op.Name.LocalName);
        Assert.Equal("103", op.Element("folioDTE")!.Value);
        Assert.Equal("XML", op.Element("formato")!.Value);
    }

    [Fact]
    public async Task GetPdf_DecodificaDobleBase64()
    {
        var (p, h) = Build("obtiene-pdf.resp.xml");
        var pdf = await p.GetPdfAsync(Ctx, 52, 103, cedible: true, copies: 2, CancellationToken.None);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        var op = BodyOp(h);
        Assert.Equal("true", op.Element("cedible")!.Value);
        Assert.Equal("2", op.Element("cantidad")!.Value);
    }

    [Fact]
    public async Task Preview_DevuelvePdfSinFolio()
    {
        var (p, h) = Build("borrador-pdf.resp.xml");
        var pdf = await p.PreviewAsync(Ctx, "<DTE/>", 52, CancellationToken.None);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal("obtieneBorrador", BodyOp(h).Name.LocalName);
    }

    [Fact]
    public async Task Test_LlamaSinConfirmar()
    {
        var (p, h) = Build("sinconfirmar-ok.resp.xml");
        await p.TestAsync(Ctx, CancellationToken.None);
        Assert.Equal("sinConfirmar", BodyOp(h).Name.LocalName);
    }

    [Fact]
    public async Task Soap_Http405_ErrorSinCredenciales()
    {
        var (p, _) = Build(null, HttpStatusCode.MethodNotAllowed);
        var ex = await Assert.ThrowsAsync<DteProviderUnavailableException>(() =>
            p.ReserveAsync(Ctx, "<DTE/>", 52, Guid.NewGuid(), CancellationToken.None));
        Assert.Contains("405", ex.Message);
        Assert.DoesNotContain(User, ex.Message);
        Assert.DoesNotContain(Pass, ex.Message);
    }

    [Fact]
    public void Capacidades_DosFasesConVistaPrevia()
    {
        var (p, _) = Build("genera-ok.resp.xml");
        Assert.Equal("FACELE", p.Code);
        Assert.True(p.Capabilities.TwoPhase);
        Assert.Contains(52, p.Capabilities.SupportedTypes);
    }
}
