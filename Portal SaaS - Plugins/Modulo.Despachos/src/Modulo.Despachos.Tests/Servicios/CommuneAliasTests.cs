using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>
/// Comunas mal digitadas en SAP: variantes típicas resueltas de fábrica y equivalencias que la compañía aprende al
/// corregir una vez (sin tocar el maestro de SAP).
/// </summary>
public class CommuneAliasTests
{
    private static async Task<MasterDataService> MasterAsync()
    {
        var db = TestDb.Create();
        var company = new FakeCurrentCompanyAccessor();
        await CommuneSeeder.EnsureSeededAsync(db, company.CompanyId);
        return new MasterDataService(db, company, new FakeCurrentUserContext());
    }

    [Theory]
    [InlineData("SANTIAGO CENTRO", "Santiago")]
    [InlineData("Stgo.", "Santiago")]
    [InlineData("STGO CENTRO", "Santiago")]
    [InlineData("Santiago-Centro", "Santiago")]
    [InlineData("PTE. ALTO", "Puente Alto")]
    [InlineData("QUINTA  NORMAL", "Quinta Normal")]
    [InlineData("sta. cruz", "Santa Cruz")]
    public async Task FindCommuneByName_VariantesTipicas_SeResuelvenDeFabrica(string search, string expected)
    {
        var svc = await MasterAsync();
        Assert.Equal(expected, (await svc.FindCommuneByNameAsync(search))?.Name);
    }

    [Fact]
    public async Task FindCommuneByName_UsaLasEquivalenciasDeLaCompania()
    {
        var svc = await MasterAsync();
        var maipu = (await svc.ListCommunesAsync()).First(c => c.Name == "Maipú");
        Assert.Null(await svc.FindCommuneByNameAsync("CIUDAD SATELITE"));

        await svc.SaveCommuneAliasAsync(new CommuneAlias { Text = "Ciudad Satélite", CommuneId = maipu.Id });

        Assert.Equal("Maipú", (await svc.FindCommuneByNameAsync("CIUDAD SATELITE"))?.Name);
        Assert.Equal("Maipú", (await svc.FindCommuneByNameAsync("ciudad satelite."))?.Name);
    }

    [Fact]
    public async Task SaveCommuneAlias_Validaciones()
    {
        var svc = await MasterAsync();
        var communes = await svc.ListCommunesAsync();
        var maipu = communes.First(c => c.Name == "Maipú");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SaveCommuneAliasAsync(new CommuneAlias { Text = "  ", CommuneId = maipu.Id }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SaveCommuneAliasAsync(new CommuneAlias { Text = "Satelite", CommuneId = 999999 }));
        // El nombre oficial (o una variante de fábrica) ya se resuelve solo: no se acepta como equivalencia.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SaveCommuneAliasAsync(new CommuneAlias { Text = "MAIPU", CommuneId = maipu.Id }));

        await svc.SaveCommuneAliasAsync(new CommuneAlias { Text = "Satelite", CommuneId = maipu.Id });
        var dup = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SaveCommuneAliasAsync(new CommuneAlias { Text = "SATÉLITE.", CommuneId = maipu.Id }));
        Assert.Contains("Ya existe", dup.Message);
    }

    [Fact]
    public async Task CommuneAlias_ListarEditarYEliminar()
    {
        var svc = await MasterAsync();
        var communes = await svc.ListCommunesAsync();
        var maipu = communes.First(c => c.Name == "Maipú");
        var cerrillos = communes.First(c => c.Name == "Cerrillos");

        var id = await svc.SaveCommuneAliasAsync(new CommuneAlias { Text = "Satelite", CommuneId = maipu.Id });
        await svc.SaveCommuneAliasAsync(new CommuneAlias { Id = id, Text = "Satelite", CommuneId = cerrillos.Id });

        var row = Assert.Single(await svc.ListCommuneAliasesAsync());
        Assert.Equal(cerrillos.Id, row.CommuneId);
        Assert.Equal("Cerrillos", (await svc.FindCommuneByNameAsync("satelite"))?.Name);

        Assert.True((await svc.DeleteCommuneAliasAsync(id)).Deleted);
        Assert.Empty(await svc.ListCommuneAliasesAsync());
    }

    [Fact]
    public async Task CorregirComuna_RecuerdaLaEquivalencia_YLaProximaFacturaEntraResuelta()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(7, commune: "VILLA NOWHERE");
        env.AddSap(8, commune: "Villa Nowhere.");
        var first = await env.Svc.CreateDraftAsync(env.Header());
        var r1 = await env.Svc.AddDocumentAsync(first, new DocumentCapture(null, DocumentKinds.Invoice, 7));
        Assert.Contains(r1.Warnings, w => w.Contains("no se pudo resolver"));
        var docId = (await env.Svc.GetAsync(first))!.Documents.Single().Id;

        await env.Svc.SetDocumentCommuneAsync(first, docId, env.CuricoId);

        var alias = Assert.Single(await env.Master.ListCommuneAliasesAsync());
        Assert.Equal("VILLA NOWHERE", alias.Text);
        Assert.Equal(env.CuricoId, alias.CommuneId);

        var second = await env.Svc.CreateDraftAsync(env.Header());
        var r2 = await env.Svc.AddDocumentAsync(second, new DocumentCapture(null, DocumentKinds.Invoice, 8));
        Assert.DoesNotContain(r2.Warnings, w => w.Contains("no se pudo resolver"));
        Assert.Equal(env.CuricoId, (await env.Svc.GetAsync(second))!.Documents.Single().DestinationCommuneId);
    }

    [Fact]
    public async Task CorregirComuna_ResuelveTambienLasOtrasFacturasDelManifiestoConElMismoTexto()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(7, commune: "VILLA NOWHERE");
        env.AddSap(8, commune: "villa nowhere");
        env.AddSap(9, commune: "OTRA NOWHERE");
        var id = await env.Svc.CreateDraftAsync(env.Header());
        foreach (var f in new long[] { 7, 8, 9 })
            await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, f));
        var docs = (await env.Svc.GetAsync(id))!.Documents;

        await env.Svc.SetDocumentCommuneAsync(id, docs.Single(d => d.Folio == 7).Id, env.CuricoId);

        var after = (await env.Svc.GetAsync(id))!.Documents;
        Assert.Equal(env.CuricoId, after.Single(d => d.Folio == 7).DestinationCommuneId);
        Assert.Equal(env.CuricoId, after.Single(d => d.Folio == 8).DestinationCommuneId);
        Assert.Null(after.Single(d => d.Folio == 9).DestinationCommuneId);
    }

    [Fact]
    public async Task CorregirComuna_SinTextoDeSap_NoGuardaEquivalencia()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(7, commune: null);
        var id = await env.Svc.CreateDraftAsync(env.Header());
        await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 7));
        var docId = (await env.Svc.GetAsync(id))!.Documents.Single().Id;

        await env.Svc.SetDocumentCommuneAsync(id, docId, env.CuricoId);

        Assert.Empty(await env.Master.ListCommuneAliasesAsync());
    }
}
