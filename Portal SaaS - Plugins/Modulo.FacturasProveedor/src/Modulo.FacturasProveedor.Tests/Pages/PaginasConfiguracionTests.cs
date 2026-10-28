using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Servicios;
using Modulo.FacturasProveedor.Tests.Servicios;
using PortalSaas.Abstractions.Modelos;
using Consultas = Modulo.FacturasProveedor.Pages.FacturasProveedor.Configuracion.ConsultasSap;
using Proveedores = Modulo.FacturasProveedor.Pages.FacturasProveedor.Configuracion.Proveedores;
using Reglas = Modulo.FacturasProveedor.Pages.FacturasProveedor.Configuracion.Reglas;

namespace Modulo.FacturasProveedor.Tests.Pages;

public class PaginasConfiguracionTests
{
    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static TempDataDictionary Temp() => new(new DefaultHttpContext(), new NullTempDataProvider());

    private static Reglas.IndexModel ReglasPage(ReviewTestEnv env) =>
        new(env.Settings, env.Inbox, env.Groups, env.User)
        {
            TempData = Temp(), PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task Reglas_GuardarSinEditar_Forbid()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.User.Grant("FacturasProveedor.reglas", PortalActions.View);
        var page = ReglasPage(env);

        Assert.IsType<PageResult>(await page.OnGetAsync(null));
        Assert.IsType<ForbidResult>(await page.OnPostGeneralAsync());
        Assert.IsType<ForbidResult>(await page.OnPostReglaAsync());
    }

    [Fact]
    public async Task Reglas_PostAvisoInvalido_MuestraError()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.User.Grant("FacturasProveedor.reglas", PortalActions.View, PortalActions.Edit);
        var page = ReglasPage(env);
        page.Rule = new Reglas.RuleForm { RuleCode = "oc-referenciada", IsActive = true, Action = RuleActions.WarnThenReject, ActionDays = 8 };

        Assert.IsType<RedirectToPageResult>(await page.OnPostReglaAsync());

        Assert.Contains("aviso", page.ErrorMessage);
    }

    [Fact]
    public async Task Reglas_AntiguedadMayorQueHistorial_MuestraAdvertencia()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.User.Grant("FacturasProveedor.reglas", PortalActions.View, PortalActions.Edit);
        var page = ReglasPage(env);
        await page.OnGetAsync(null);
        Assert.Null(page.CoverageWarning);

        page.Rule = new Reglas.RuleForm
        {
            RuleCode = "oc-antiguedad", IsActive = true, Action = RuleActions.Reject,
            Parameters = new Dictionary<string, string> { ["max_days"] = "90" },
        };
        await page.OnPostReglaAsync();
        await page.OnGetAsync(null);

        Assert.Contains("60 días", page.CoverageWarning);
    }

    [Fact]
    public async Task Proveedores_SinCrear_Forbid()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.User.Grant("FacturasProveedor.proveedores", PortalActions.View, PortalActions.Edit);
        var page = new Proveedores.IndexModel(env.Profiles, env.Groups, env.Sap, env.Settings, env.Inbox, env.Engine, env.User)
        {
            TempData = Temp(), PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
        };
        page.Input = new Proveedores.ProfileForm { TaxId = "77555010-K", Condition = SupplierConditions.Custom, ValidFrom = "2026-11-01" };

        Assert.IsType<ForbidResult>(await page.OnPostSaveAsync());
        Assert.IsType<PageResult>(await page.OnGetAsync(null));
    }

    [Fact]
    public async Task ConsultasSap_ProbarSinEditar_Forbid()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.User.Grant("FacturasProveedor.consultas-sap", PortalActions.View);
        var page = new Consultas.IndexModel(env.Sap, env.User) { TempData = Temp() };

        Assert.IsType<ForbidResult>(await page.OnPostProbarAsync(SapQueryKinds.PurchaseOrderByDocNum, "SELECT 1 FROM OPOR WHERE \"DocNum\" = :doc_num", "4512"));
        Assert.IsType<ForbidResult>(await page.OnPostSaveAsync(SapQueryKinds.PurchaseOrderByDocNum, "SELECT 1", true));
    }
}
