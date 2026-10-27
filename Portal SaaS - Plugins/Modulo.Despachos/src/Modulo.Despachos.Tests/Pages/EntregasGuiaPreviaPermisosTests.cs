using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Servicios;
using PortalSaas.Abstractions.Modelos;
using Entregas = Modulo.Despachos.Pages.Despachos.EntregasGuiaPrevia;

namespace Modulo.Despachos.Tests.Pages;

/// <summary>Gates de permisos y defaults de las pantallas de Entregas con guía previa.</summary>
public class EntregasGuiaPreviaPermisosTests
{
    private const string Menu = "Despachos.entregas-guia-previa";

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static TempDataDictionary Temp() => new(new DefaultHttpContext(), new NullTempDataProvider());

    private static DispatchGroupPdfService Pdf(ManifestTestEnv env) => new(env.Groups, env.Company);

    private static Entregas.DetalleModel Detalle(ManifestTestEnv env) =>
        new(env.Groups, env.Deliveries, Pdf(env), env.User) { TempData = Temp() };

    [Fact]
    public void Menu_EntregasGuiaPrevia_ConIconoYRuta_SegundoEnOperacion()
    {
        var menu = new ModuloDespachos().GetMenu().ToList();
        var item = menu.Single(i => i.Code == "entregas-guia-previa");
        Assert.Equal(("grupo-operacion", "Entregas con guía previa", "bi-box-arrow-right", "/despachos/entregas-guia-previa", 2),
            (item.ParentCode, item.Name, item.Icon, item.PageRoute, item.Order));
        Assert.Equal(new[] { "agrupar-traslados", "entregas-guia-previa", "manifiestos", "guias-manuales", "retorno", "reporte-documentos" },
            menu.Where(i => i.ParentCode == "grupo-operacion").OrderBy(i => i.Order).Select(i => i.Code));
    }

    [Fact]
    public async Task Index_SinView_Forbid()
    {
        var env = DeliveryGroupServiceTests.Env();
        var page = new Entregas.IndexModel(env.Groups, Pdf(env), env.User) { TempData = Temp() };
        Assert.IsType<ForbidResult>(await page.OnGetAsync());

        env.User.Grant(Menu, PortalActions.View);
        Assert.IsType<PageResult>(await page.OnGetAsync());
    }

    [Fact]
    public async Task Index_ListaSoloEntregas()
    {
        var env = DeliveryGroupServiceTests.Env();
        await env.Deliveries.GenerateAsync(DeliveryGroupServiceTests.Input(701, 703));
        env.User.Grant(Menu, PortalActions.View);
        var page = new Entregas.IndexModel(env.Groups, Pdf(env), env.User) { TempData = Temp(), From = "2026-11-10", To = "2026-11-10" };

        await page.OnGetAsync();

        Assert.Equal(new[] { "EG-000001", "EG-000002" }, page.Items.Select(x => x.Code).OrderBy(x => x));
    }

    [Fact]
    public async Task Generar_SinCreate_Forbid()
    {
        var env = DeliveryGroupServiceTests.Env();
        env.User.Grant(Menu, PortalActions.View);
        var page = new Entregas.GenerarModel(env.Deliveries, env.User) { TempData = Temp() };
        Assert.IsType<ForbidResult>(await page.OnGetAsync());
        Assert.IsType<ForbidResult>(await page.OnPostGenerarAsync("C001", "2026-11-01", "2026-11-10", 2, 2, new[] { 701 }));
    }

    [Fact]
    public async Task Generar_DefaultsIndicador2Tipo2()
    {
        var env = DeliveryGroupServiceTests.Env();
        env.User.Grant(Menu, PortalActions.View, PortalActions.Create);
        var page = new Entregas.GenerarModel(env.Deliveries, env.User) { TempData = Temp() };

        Assert.IsType<PageResult>(await page.OnGetAsync());

        Assert.Equal((2, 2), (page.TransferIndicator, page.DispatchType));
        Assert.Null(page.Candidates);
    }

    [Fact]
    public async Task Generar_ConCliente_ListaYAgrupa()
    {
        var env = DeliveryGroupServiceTests.Env();
        env.User.Grant(Menu, PortalActions.View, PortalActions.Create);
        var page = new Entregas.GenerarModel(env.Deliveries, env.User) { TempData = Temp(), Customer = "C001", FromDate = "2026-11-01" };

        await page.OnGetAsync();
        Assert.Equal(2, page.Candidates!.Orders.Count);

        Assert.IsType<RedirectToPageResult>(await page.OnPostGenerarAsync("C001", "2026-11-01", "2026-11-10", 2, 2, new[] { 701, 702 }));
        Assert.Equal(1, await env.Db.DispatchGroups.CountAsync(g => g.Kind == DispatchGroupKinds.Delivery));
        Assert.Contains("1 agrupación", page.SuccessMessage);
    }

    [Fact]
    public async Task Detalle_Condiciones_SinEdit_Forbid()
    {
        var env = DeliveryGroupServiceTests.Env();
        var g = Assert.Single(await env.Deliveries.GenerateAsync(DeliveryGroupServiceTests.Input(701)));
        env.User.Grant(Menu, PortalActions.View);
        var page = Detalle(env);

        Assert.IsType<PageResult>(await page.OnGetAsync(g.Id));
        Assert.IsType<ForbidResult>(await page.OnPostCondicionesAsync(g.Id, 1, 3));

        env.User.Grant(Menu, PortalActions.Edit);
        Assert.IsType<RedirectToPageResult>(await page.OnPostCondicionesAsync(g.Id, 1, 3));
        Assert.Equal(1, (await env.Groups.GetAsync(g.Id))!.TransferIndicator);
    }

    [Fact]
    public async Task Detalle_Anular_SinDelete_Forbid()
    {
        var env = DeliveryGroupServiceTests.Env();
        var g = Assert.Single(await env.Deliveries.GenerateAsync(DeliveryGroupServiceTests.Input(701)));
        env.User.Grant(Menu, PortalActions.View, PortalActions.Edit);
        var page = Detalle(env);

        Assert.IsType<ForbidResult>(await page.OnPostAnularAsync(g.Id, "error"));

        env.User.Grant(Menu, PortalActions.Delete);
        Assert.IsType<RedirectToPageResult>(await page.OnPostAnularAsync(g.Id, "error"));
        Assert.Equal(DispatchGroupStatuses.Annulled, (await env.Groups.GetAsync(g.Id))!.Status);
    }

    [Fact]
    public async Task Detalle_AgrupacionDeTraslados_NoSeAbreAqui()
    {
        var env = await DispatchGroupServiceTests.EnvAsync();
        var t = Assert.Single(await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, DispatchGroupServiceTests.Desde, new[] { 501 }));
        env.User.Grant(Menu, PortalActions.View);

        Assert.IsType<NotFoundResult>(await Detalle(env).OnGetAsync(t.Id));
    }
}
