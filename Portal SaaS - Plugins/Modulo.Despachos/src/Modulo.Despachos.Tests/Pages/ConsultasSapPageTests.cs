using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;
using PortalSaas.Abstractions.Modelos;
using ConsultasSap = Modulo.Despachos.Pages.Despachos.Configuracion.ConsultasSap;

namespace Modulo.Despachos.Tests.Pages;

public class ConsultasSapPageTests
{
    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static (ConsultasSap.IndexModel Page, FakeHanaService Hana) Build(FakeCurrentUserContext user)
    {
        var hana = new FakeHanaService { EngineType = "hana" };
        var svc = new SapDocumentLookupService(TestDb.Create(), new FakeCurrentCompanyAccessor(), user, hana);
        var page = new ConsultasSap.IndexModel(svc, user)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider()),
        };
        return (page, hana);
    }

    [Fact]
    public async Task OnGet_SinView_Forbid_YProbarSinEdicion_Forbid()
    {
        var (page, _) = Build(new FakeCurrentUserContext());
        Assert.IsType<ForbidResult>(await page.OnGetAsync());
        Assert.IsType<ForbidResult>(await page.OnPostProbeAsync());

        var soloVer = Build(new FakeCurrentUserContext().Grant("Despachos.consultas-sap", PortalActions.View)).Page;
        Assert.IsType<ForbidResult>(await soloVer.OnPostProbeAsync());
        soloVer.Input = new ConsultasSap.IndexModel.ConsultaInput { Id = 3, DocumentKind = DocumentKinds.Invoice, SqlText = "SELECT 1 WHERE :folio = 1", SampleFolio = 1 };
        Assert.IsType<ForbidResult>(await soloVer.OnPostSaveAsync());
    }

    [Fact]
    public async Task OnGet_ConView_ListaAmbosTipos_ConSeedEnHana()
    {
        var (page, _) = Build(new FakeCurrentUserContext().Grant("Despachos.consultas-sap", PortalActions.View));
        Assert.IsType<PageResult>(await page.OnGetAsync());
        Assert.Equal(2, page.Rows.Count);
        Assert.All(page.Rows, r => Assert.NotNull(r.Query));
    }

    [Fact]
    public async Task OnPostProbe_MuestraErroresDeValidacion_SinGuardar()
    {
        var (page, hana) = Build(new FakeCurrentUserContext().Grant("Despachos.consultas-sap", PortalActions.View, PortalActions.Edit));
        page.Input = new ConsultasSap.IndexModel.ConsultaInput
        {
            DocumentKind = DocumentKinds.Invoice, SqlText = "DELETE FROM OINV WHERE :folio = 1", SampleFolio = 1500,
        };

        Assert.IsType<PageResult>(await page.OnPostProbeAsync());

        Assert.False(page.Probe!.Validation.IsValid);
        Assert.NotNull(page.Probe.Validation.Error);
        Assert.Empty(hana.ExecutedSql);
    }

    [Fact]
    public async Task OnPostSave_SinFolioDePrueba_ReportaErrorYNoEjecuta()
    {
        var (page, hana) = Build(new FakeCurrentUserContext().Grant("Despachos.consultas-sap", PortalActions.View, PortalActions.Edit));
        page.Input = new ConsultasSap.IndexModel.ConsultaInput { Id = 1, DocumentKind = DocumentKinds.Invoice, SqlText = "SELECT 1 WHERE :folio = 1" };

        await page.OnPostSaveAsync();

        Assert.Contains("folio de prueba", page.ErrorMessage);
        Assert.Empty(hana.ExecutedSql);
    }
}
