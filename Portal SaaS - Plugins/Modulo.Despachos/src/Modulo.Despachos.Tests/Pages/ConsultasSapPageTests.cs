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
        soloVer.Input = new ConsultasSap.IndexModel.ConsultaInput { Id = 3, DocumentKind = DocumentKinds.Invoice, SqlText = "SELECT 1 WHERE :folio = 1", Sample = "1" };
        Assert.IsType<ForbidResult>(await soloVer.OnPostSaveAsync());
    }

    [Fact]
    public async Task OnGet_ConView_ListaLasOchoConsultas_ConSeedEnHana()
    {
        var (page, _) = Build(new FakeCurrentUserContext().Grant("Despachos.consultas-sap", PortalActions.View));
        Assert.IsType<PageResult>(await page.OnGetAsync());
        Assert.Equal(8, page.Rows.Count);
        Assert.Contains(page.Rows, r => r.Kind == SapQueryKinds.InvoiceLines);
        Assert.All(page.Rows, r => Assert.NotNull(r.Query));
    }

    [Fact]
    public async Task OnPostProbe_MuestraErroresDeValidacion_SinGuardar()
    {
        var (page, hana) = Build(new FakeCurrentUserContext().Grant("Despachos.consultas-sap", PortalActions.View, PortalActions.Edit));
        page.Input = new ConsultasSap.IndexModel.ConsultaInput
        {
            DocumentKind = DocumentKinds.Invoice, SqlText = "DELETE FROM OINV WHERE :folio = 1", Sample = "1500",
        };

        Assert.IsType<PageResult>(await page.OnPostProbeAsync());

        Assert.False(page.Probe!.Validation.IsValid);
        Assert.NotNull(page.Probe.Validation.Error);
        Assert.Empty(hana.ExecutedSql);
    }

    [Fact]
    public async Task OnPostProbe_TransferenciaPorLpn_UsaMuestraTexto()
    {
        var (page, hana) = Build(new FakeCurrentUserContext().Grant("Despachos.consultas-sap", PortalActions.View, PortalActions.Edit));
        const string sql = "SELECT 1 FROM DUMMY WHERE :lpn = :lpn";
        hana.Results[sql] = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?>
            {
                ["doc_entry"] = 501, ["doc_num"] = 1501, ["document_date"] = new DateTime(2026, 10, 20), ["warehouse_code"] = "TDA01",
                ["warehouse_name"] = "Tienda", ["destination_address"] = null, ["destination_commune"] = null, ["units"] = 3m,
                ["is_canceled"] = "N", ["lpn"] = "CTBO123",
            },
        };
        page.Input = new ConsultasSap.IndexModel.ConsultaInput { DocumentKind = SapQueryKinds.TransferByLpn, SqlText = sql, Sample = " CTBO123 " };

        await page.OnPostProbeAsync();

        Assert.True(page.Probe!.Validation.IsValid, page.Probe.Validation.Error);
        Assert.Equal("CTBO123", hana.LastParameters!.GetType().GetProperty("lpn")!.GetValue(hana.LastParameters));
    }

    [Fact]
    public async Task OnPostProbe_TransferenciaPorLpn_SinMuestra_PideElLpn()
    {
        var (page, hana) = Build(new FakeCurrentUserContext().Grant("Despachos.consultas-sap", PortalActions.View, PortalActions.Edit));
        page.Input = new ConsultasSap.IndexModel.ConsultaInput { DocumentKind = SapQueryKinds.TransferByLpn, SqlText = "SELECT 1 WHERE :lpn = 1" };

        await page.OnPostProbeAsync();

        Assert.Contains("LPN de prueba", page.ErrorMessage);
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
