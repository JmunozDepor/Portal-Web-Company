using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;
using Modulo.Despachos.Tests.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Manifiestos = Modulo.Despachos.Pages.Despachos.Manifiestos;
using PreGuias = Modulo.Despachos.Pages.Despachos.PreGuias;

namespace Modulo.Despachos.Tests.Pages;

/// <summary>Gates de permisos de las pantallas de pre-guías y la hoja escaneada en el manifiesto.</summary>
public class PreGuiasPermisosTests
{
    private const string Menu = "Despachos.pre-guias";

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class FakeContactLookup : IUserContactLookupService
    {
        public Task<UserContactDto?> GetContactAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<UserContactDto?>(new UserContactDto(Guid.Empty, "u@x.cl", false));
    }

    private static TempDataDictionary Temp() => new(new DefaultHttpContext(), new NullTempDataProvider());

    private static DispatchGroupPdfService Pdf(ManifestTestEnv env) => new(env.Groups, env.Company);

    [Fact]
    public void Menu_PreGuias_ConIconoYRuta()
    {
        var item = new ModuloDespachos().GetMenu().Single(i => i.Code == "pre-guias");
        Assert.Equal(("grupo-operacion", "bi-box-seam", "/despachos/pre-guias"), (item.ParentCode, item.Icon, item.PageRoute));
    }

    [Fact]
    public async Task Index_SinView_Forbid()
    {
        var env = await DispatchGroupServiceTests.EnvAsync();
        var page = new PreGuias.IndexModel(env.Groups, Pdf(env), env.Master, env.User) { TempData = Temp() };
        Assert.IsType<ForbidResult>(await page.OnGetAsync());

        env.User.Grant(Menu, PortalActions.View);
        Assert.IsType<PageResult>(await page.OnGetAsync());
    }

    [Fact]
    public async Task Generar_SinCreate_Forbid()
    {
        var env = await DispatchGroupServiceTests.EnvAsync();
        env.User.Grant(Menu, PortalActions.View);
        var page = new PreGuias.GenerarModel(env.Groups, env.Master, env.User) { TempData = Temp() };
        Assert.IsType<ForbidResult>(await page.OnGetAsync());
        Assert.IsType<ForbidResult>(await page.OnPostGenerarAsync("2026-11-10", "2026-11-01", new[] { 501 }));
    }

    [Fact]
    public async Task Generar_Post_CreaYRedirige()
    {
        var env = await DispatchGroupServiceTests.EnvAsync();
        env.User.Grant(Menu, PortalActions.View, PortalActions.Create);
        var page = new PreGuias.GenerarModel(env.Groups, env.Master, env.User) { TempData = Temp() };

        Assert.IsType<PageResult>(await page.OnGetAsync());
        Assert.Equal(2, page.Candidates.Count);
        Assert.IsType<RedirectToPageResult>(await page.OnPostGenerarAsync("2026-11-10", "2026-11-01", new[] { 501, 502 }));

        Assert.Equal(1, await env.Db.DispatchGroups.CountAsync());
        Assert.Contains("1 pre-guía", page.SuccessMessage);
    }

    [Fact]
    public async Task Detalle_AgregarLpn_SinEdit_Forbid()
    {
        var env = await DispatchGroupServiceTests.EnvAsync();
        var g = Assert.Single(await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, DispatchGroupServiceTests.Desde, new[] { 501 }));
        env.User.Grant(Menu, PortalActions.View);
        var page = new PreGuias.DetalleModel(env.Groups, Pdf(env), env.User) { TempData = Temp() };

        Assert.IsType<PageResult>(await page.OnGetAsync(g.Id));
        Assert.IsType<ForbidResult>(await page.OnPostAgregarLpnAsync(g.Id, "CTBO502"));

        env.User.Grant(Menu, PortalActions.Edit);
        Assert.IsType<RedirectToPageResult>(await page.OnPostAgregarLpnAsync(g.Id, "CTBO502"));
        Assert.Equal(2, (await env.Groups.GetAsync(g.Id))!.Transfers.Count);
    }

    [Fact]
    public async Task Detalle_Anular_SinDelete_Forbid()
    {
        var env = await DispatchGroupServiceTests.EnvAsync();
        var g = Assert.Single(await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, DispatchGroupServiceTests.Desde, new[] { 501 }));
        env.User.Grant(Menu, PortalActions.View, PortalActions.Edit);
        var page = new PreGuias.DetalleModel(env.Groups, Pdf(env), env.User) { TempData = Temp() };

        Assert.IsType<ForbidResult>(await page.OnPostAnularAsync(g.Id, "error"));

        env.User.Grant(Menu, PortalActions.Delete);
        Assert.IsType<RedirectToPageResult>(await page.OnPostAnularAsync(g.Id, "error"));
    }

    [Fact]
    public async Task Detalle_Pdf_ConView()
    {
        var env = await DispatchGroupServiceTests.EnvAsync();
        var g = Assert.Single(await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, DispatchGroupServiceTests.Desde, new[] { 501 }));
        var page = new PreGuias.DetalleModel(env.Groups, Pdf(env), env.User) { TempData = Temp() };
        Assert.IsType<ForbidResult>(await page.OnGetPdfAsync(g.Id));

        env.User.Grant(Menu, PortalActions.View);
        Assert.IsAssignableFrom<FileContentResult>(await page.OnGetPdfAsync(g.Id));
    }

    [Fact]
    public async Task Manifiesto_EscanearHoja_DevuelveReload()
    {
        var env = await DispatchGroupServiceTests.EnvAsync();
        await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, DispatchGroupServiceTests.Desde, new[] { 501, 502 });
        env.User.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Edit);
        var pdf = new ManifestPdfService(env.Db, env.Company, env.User, env.Svc, env.Clock);
        var page = new Manifiestos.DetalleModel(env.Svc, pdf, env.Master, env.User, new FakeContactLookup(), env.Cost, env.Guides) { TempData = Temp() };
        var id = await env.Svc.CreateDraftAsync(env.Header());

        var json = Assert.IsType<JsonResult>(await page.OnPostAgregarDocumentoAsync(id, new Manifiestos.DetalleModel.ScanInput { RawScan = "PG-000001" }));

        object? Prop(string name) => json.Value!.GetType().GetProperty(name)!.GetValue(json.Value);
        Assert.Equal(true, Prop("reload"));
        Assert.Contains("transferencias agregadas", (string)Prop("message")!);
    }
}
