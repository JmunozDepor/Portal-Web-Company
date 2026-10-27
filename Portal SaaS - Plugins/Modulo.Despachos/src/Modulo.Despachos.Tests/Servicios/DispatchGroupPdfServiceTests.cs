using System.Text;
using Modulo.Despachos.Servicios;
using PdfSharp.Pdf.IO;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Hoja de agrupación: una página por agrupación, con Code128, LPN y unidades; nunca montos.</summary>
public class DispatchGroupPdfServiceTests
{
    private static async Task<(ManifestTestEnv Env, DispatchGroupPdfService Pdf, List<long> Ids)> ArrangeAsync()
    {
        var env = await DispatchGroupServiceTests.EnvAsync();
        var groups = await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, DispatchGroupServiceTests.Desde, new[] { 501, 502, 503 });
        return (env, new DispatchGroupPdfService(env.Groups, env.Company), groups.Select(g => g.Id).ToList());
    }

    [Fact]
    public async Task Render_PdfValido_UnaPaginaPorAgrupacion()
    {
        var (_, pdf, ids) = await ArrangeAsync();

        var bytes = await pdf.RenderAsync(ids);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        using var ms = new MemoryStream(bytes);
        Assert.Equal(2, PdfReader.Open(ms, PdfDocumentOpenMode.Import).PageCount);
        // MigraDoc corta las palabras en el guion: se compara sin espacios.
        var text = string.Concat(ManifestPdfServiceTests.ExtractText(bytes).Where(c => !char.IsWhiteSpace(c)));
        Assert.Contains("AT-000001", text);
        Assert.Contains("AT-000002", text);
        Assert.Contains("CTBO501", text);
        Assert.Contains("10502", text);
    }

    [Fact]
    public async Task Render_SinMontos_ConTotales()
    {
        var (_, pdf, ids) = await ArrangeAsync();

        var text = ManifestPdfServiceTests.ExtractText(await pdf.RenderAsync(ids.Take(1).ToList()));

        Assert.Contains("LPN", text);
        Assert.Contains("Unidades", text);
        Assert.Contains("Total", text);
        Assert.Contains("17", text);
        Assert.DoesNotContain("$", text);
        Assert.DoesNotContain("Monto", text);
    }

    [Fact]
    public async Task Render_ListaVacia_Lanza()
    {
        var (_, pdf, _) = await ArrangeAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pdf.RenderAsync(Array.Empty<long>()));
        Assert.Contains("No hay agrupaciones para imprimir", ex.Message);
    }

    [Fact]
    public async Task Hoja_Entrega_MuestraOcYCodigoEG()
    {
        var env = DeliveryGroupServiceTests.Env();
        var g = (await env.Deliveries.GenerateAsync(DeliveryGroupServiceTests.Input(701, 702))).Single();

        var text = string.Concat(ManifestPdfServiceTests.ExtractText(
            await new DispatchGroupPdfService(env.Groups, env.Company).RenderAsync(new[] { g.Id })).Where(c => !char.IsWhiteSpace(c)));

        Assert.Contains("EG-000001", text);
        Assert.Contains("4500000001", text);
        Assert.Contains("Falabella", text);
        Assert.Contains("LPN701", text);
        Assert.DoesNotContain("AT-", text);
        Assert.DoesNotContain("$", text);
    }
}
