using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Servicios;
using PortalSaas.Abstractions.Modelos;
using Cierre = Modulo.Despachos.Pages.Despachos.CierreMensual;
using Reportes = Modulo.Despachos.Pages.Despachos.Reportes;

namespace Modulo.Despachos.Tests.Pages;

/// <summary>Gates de permisos de Cierre mensual y Reporte mensual.</summary>
public class CierreReportePermisosTests
{
    private sealed class NullTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static TempDataDictionary Temp() => new(new DefaultHttpContext(), new NullTempData());

    [Fact]
    public async Task Cerrar_SinApprove_Forbid_ConApprove_Cierra()
    {
        var env = ManifestTestEnv.Build();
        await MonthlyClosureServiceTests.SeedManifestAsync(env, 1, new DateOnly(2026, 9, 1));
        env.User.Grant("Despachos.cierre-mensual", PortalActions.View);
        var page = new Cierre.IndexModel(env.Closure, env.User) { TempData = Temp(), Period = "2026-09" };
        Assert.IsType<ForbidResult>(await page.OnPostCerrarAsync());
        Assert.False(await env.Closure.IsClosedAsync(new DateOnly(2026, 9, 1)));

        env.User.Grant("Despachos.cierre-mensual", PortalActions.Approve);
        Assert.IsType<RedirectToPageResult>(await page.OnPostCerrarAsync());
        Assert.True(await env.Closure.IsClosedAsync(new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public async Task Reabrir_SoloAdmin()
    {
        var env = ManifestTestEnv.Build();
        await MonthlyClosureServiceTests.SeedManifestAsync(env, 1, new DateOnly(2026, 9, 1));
        await env.Closure.CloseAsync(2026, 9);
        env.User.Grant("Despachos.cierre-mensual", PortalActions.View, PortalActions.Approve);
        var page = new Cierre.IndexModel(env.Closure, env.User) { TempData = Temp(), Period = "2026-09" };
        Assert.IsType<ForbidResult>(await page.OnPostReabrirAsync("x"));

        env.User.IsAdmin = true;
        Assert.IsType<RedirectToPageResult>(await page.OnPostReabrirAsync("x"));
        Assert.False(await env.Closure.IsClosedAsync(new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public async Task Exportar_SinExport_Forbid_ConExport_Archivo()
    {
        var env = ManifestTestEnv.Build();
        env.User.Grant("Despachos.reportes", PortalActions.View);
        var page = new Reportes.MensualModel(new DispatchReportService(env.Db, env.Company), env.User)
            { TempData = Temp(), Period = "2026-09" };
        Assert.IsType<ForbidResult>(await page.OnGetExportarAsync());

        env.User.Grant("Despachos.reportes", PortalActions.Export);
        var file = Assert.IsType<FileContentResult>(await page.OnGetExportarAsync());
        Assert.Equal("despachos-2026-09.xlsx", file.FileDownloadName);
    }
}
