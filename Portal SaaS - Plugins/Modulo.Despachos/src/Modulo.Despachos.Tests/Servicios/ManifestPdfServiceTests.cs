using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace Modulo.Despachos.Tests.Servicios;

public class ManifestPdfServiceTests
{
    private static ManifestPdfService Pdf(ManifestTestEnv env) =>
        new(env.Db, env.Company, env.User, env.Svc, env.Clock);

    /// <summary>Texto literal de todas las páginas (operadores Tj/TJ del content stream).</summary>
    internal static string ExtractText(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        var sb = new StringBuilder();
        foreach (var page in doc.Pages)
        {
            var seq = ContentReader.ReadContent(page);
            Collect(seq, sb);
        }
        return sb.ToString();
    }

    private static void Collect(CSequence seq, StringBuilder sb)
    {
        foreach (var o in seq)
        {
            switch (o)
            {
                case COperator op:
                    foreach (var operand in op.Operands)
                    {
                        if (operand is CString s) sb.Append(s.Value);
                        else if (operand is CArray arr) Collect(arr, sb);
                    }
                    sb.Append(' ');
                    break;
                case CString s2: sb.Append(s2.Value); break;
                case CSequence inner: Collect(inner, sb); break;
            }
        }
    }

    private static async Task<(ManifestTestEnv Env, Guid Id)> DispatchedAsync()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(3, commune: "Curicó");
        env.AddSap(1);
        env.AddSap(2);
        var id = await env.DraftWithDocsAsync(3, 1, 2);
        Assert.True((await env.Svc.DispatchAsync(id)).Success);
        return (env, id);
    }

    [Fact]
    public async Task Render_DevuelvePdfValido()
    {
        var (env, id) = await DispatchedAsync();
        var bytes = await Pdf(env).RenderAsync(id, false);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task Render_IncluyeNombreDeLaCompania_YMontos()
    {
        var (env, id) = await DispatchedAsync();
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("CompanyName", "Depor Testing") }, "test")),
            },
        };
        var bytes = await new ManifestPdfService(env.Db, env.Company, env.User, env.Svc, env.Clock, http).RenderAsync(id, false);
        var dump = Environment.GetEnvironmentVariable("DESPACHOS_PDF_DUMP");
        if (!string.IsNullOrEmpty(dump)) File.WriteAllBytes(dump, bytes);
        var text = ExtractText(bytes);
        Assert.Contains("Depor", text); // MigraDoc escribe cada palabra por separado.
        Assert.Contains("Testing", text);
        Assert.Contains("Monto", text);
        Assert.Contains("$119.000", text);
        Assert.Contains("$357.000", text);
    }

    [Fact]
    public async Task Render_Copia_IncluyeMarcaCopia_YOriginalNo()
    {
        var (env, id) = await DispatchedAsync();
        var copia = ExtractText(await Pdf(env).RenderAsync(id, true));
        var original = ExtractText(await Pdf(env).RenderAsync(id, false));
        Assert.Contains("COPIA", copia);
        Assert.Contains("tester", copia);
        Assert.DoesNotContain("COPIA", original);
        Assert.Contains("Rancagua", original);
    }

    [Fact]
    public async Task BuildModel_AgrupaPorDestino_ConSubtotalesYTotales()
    {
        var (env, id) = await DispatchedAsync();
        var m = (await env.Svc.GetAsync(id))!;
        var model = ManifestPdfService.BuildModel(m, new Dictionary<long, string>
        { [env.RancaguaId] = "Rancagua", [env.CuricoId] = "Curicó" });

        Assert.Equal(2, model.Groups.Count);
        Assert.Equal("Curicó", model.Groups[0].Title);          // orden alfabético
        Assert.Equal("Rancagua", model.Groups[1].Title);
        Assert.Equal(new long[] { 1, 2 }, model.Groups[1].Rows.Select(r => r.Folio));   // por folio
        Assert.Equal(2, model.Groups[1].Documents);
        Assert.Equal(20m, model.Groups[1].Units);
        Assert.Equal(8, model.Groups[1].Packages);
        Assert.Equal(3, model.TotalDocuments);
        Assert.Equal(30m, model.TotalUnits);
        Assert.Equal(12, model.TotalPackages);
    }

    [Fact]
    public void BuildModel_ComunaNoResuelta_UsaTextoOriginal()
    {
        var m = new Manifest { Number = 5 };
        m.Documents.Add(new ManifestDocument
        { Folio = 9, DocumentKind = DocumentKinds.Invoice, DestinationCommuneName = "Villa Nowhere", DestinationCommuneId = null, Units = 1, Packages = 1 });
        var model = ManifestPdfService.BuildModel(m, new Dictionary<long, string>());
        Assert.Equal("Comuna no resuelta: Villa Nowhere", model.Groups[0].Title);
    }

    [Fact]
    public async Task Render_RegistraEventoPrinted_YReprinted()
    {
        var (env, id) = await DispatchedAsync();
        await Pdf(env).RenderAsync(id, false);
        await Pdf(env).RenderAsync(id, true);
        var ev = await env.Db.ManifestEvents.Where(e => e.ManifestId == id).ToListAsync();
        var printed = Assert.Single(ev, e => e.EventType == ManifestEventTypes.Printed);
        var reprinted = Assert.Single(ev, e => e.EventType == ManifestEventTypes.Reprinted);
        Assert.Equal(env.User.UserId, printed.UserId);
        Assert.Equal(env.User.UserId, reprinted.UserId);
    }

    [Fact]
    public async Task Render_ManifiestoInexistente_Lanza()
    {
        var env = ManifestTestEnv.Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Pdf(env).RenderAsync(Guid.NewGuid(), false));
    }

    [Fact]
    public void Code128Png_DevuelvePng()
    {
        var png = BarcodeRenderer.Code128Png("12345");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
    }
}
