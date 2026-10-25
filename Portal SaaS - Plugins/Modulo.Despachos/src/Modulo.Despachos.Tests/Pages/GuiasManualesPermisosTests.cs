using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Guias = Modulo.Despachos.Pages.Despachos.GuiasManuales;
using Manifiestos = Modulo.Despachos.Pages.Despachos.Manifiestos;
using Motivos = Modulo.Despachos.Pages.Despachos.Configuracion.MotivosTraslado;

namespace Modulo.Despachos.Tests.Pages;

/// <summary>Pantallas de guías: lista, guía manual (formulario, detalle, importación), motivos y el selector del manifiesto.</summary>
public class GuiasManualesPermisosTests
{
    private const string Menu = "Despachos.guias-manuales";

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

    private static Guias.EditarModel Editar(ManifestTestEnv env) =>
        new(env.ManualGuides, env.Master, env.User) { TempData = Temp(), PageContext = new PageContext { HttpContext = new DefaultHttpContext() } };

    private static Guias.GuiaForm Form(ManifestTestEnv env) => new()
    {
        IssueDate = "2026-11-10", TransferIndicator = 6, DispatchType = 3, OriginId = env.OriginId,
        ReceiverTaxId = "17.183.714-6", ReceiverName = "Freddi Fuentes", ReceiverActivity = "Tienda", ReceiverAddress = "Circunvalación 1055",
        ReceiverCommune = "Rancagua", DestinationAddress = "Circunvalación 1055", DestinationCommune = "Rancagua",
        TransportMode = ManualTransportModes.Master, VehicleId = env.VehicleId, DriverId = env.DriverId, CarrierId = env.CarrierId,
        DepartureDate = "2026-11-10", DepartureTime = "09:30", ArrivalDate = "2026-11-11",
        Lines = { new Guias.LineForm { Description = "Mueble", Quantity = "1", UnitPrice = "500000" } },
    };

    [Fact]
    public void Menu_GuiasManuales_ConIconoYRuta()
    {
        var item = new ModuloDespachos().GetMenu().Single(i => i.Code == "guias-manuales");
        Assert.Equal(("grupo-operacion", "bi-file-earmark-text", "/despachos/guias-manuales"), (item.ParentCode, item.Icon, item.PageRoute));
    }

    [Fact]
    public void Menu_MotivosTraslado_ConIcono()
    {
        var item = new ModuloDespachos().GetMenu().Single(i => i.Code == "motivos-traslado");
        Assert.Equal(("grupo-config", "bi-arrow-left-right", "/despachos/configuracion/motivos-traslado"), (item.ParentCode, item.Icon, item.PageRoute));
    }

    [Fact]
    public async Task Index_SinView_Forbid()
    {
        var env = ManifestTestEnv.Build();
        var page = new Guias.IndexModel(env.ManualGuides, env.User) { TempData = Temp() };
        Assert.IsType<ForbidResult>(await page.OnGetAsync());
        env.User.Grant(Menu, PortalActions.View);
        Assert.IsType<PageResult>(await page.OnGetAsync());
    }

    [Fact]
    public async Task Editar_Nueva_SinCreate_Forbid()
    {
        var env = ManifestTestEnv.Build();
        env.User.Grant(Menu, PortalActions.View);
        var page = Editar(env);
        Assert.IsType<ForbidResult>(await page.OnGetAsync(null));
        page.Form = Form(env);
        Assert.IsType<ForbidResult>(await page.OnPostGuardarAsync());
    }

    [Fact]
    public async Task Editar_Guardar_CreaYRedirigeAlDetalle()
    {
        var env = ManifestTestEnv.Build();
        env.User.Grant(Menu, PortalActions.View, PortalActions.Create);
        var page = Editar(env);
        Assert.IsType<PageResult>(await page.OnGetAsync(null));
        page.Form = Form(env);

        var r = Assert.IsType<RedirectToPageResult>(await page.OnPostGuardarAsync());

        Assert.Equal("Detalle", r.PageName);
        var g = Assert.Single(await env.ManualGuides.ListAsync(new GuideFilter(null, null, null, null, null)));
        Assert.Equal(595000m, g.TotalAmount);
        var input = await env.ManualGuides.GetInputAsync(g.Id);
        Assert.Equal(("AB1234", "12345678-5", "76543210-3"), (input.Transport!.Plate, input.Transport.DriverTaxId, input.Transport.CarrierTaxId));
        Assert.Equal((new DateOnly(2026, 11, 10), new TimeOnly(9, 30), new DateOnly(2026, 11, 11)),
            (input.Transport.DepartureDate, input.Transport.DepartureTime, input.Transport.ArrivalDate));
    }

    [Fact]
    public async Task Editar_Guardar_Invalida_VuelveALaPaginaConError()
    {
        var env = ManifestTestEnv.Build();
        env.User.Grant(Menu, PortalActions.View, PortalActions.Create);
        var page = Editar(env);
        page.Form = Form(env);
        page.Form.ReceiverTaxId = "1-1";
        Assert.IsType<PageResult>(await page.OnPostGuardarAsync());
        Assert.Contains("RUT del receptor", page.ErrorMessage);
    }

    [Fact]
    public async Task Editar_Existente_SinEdit_Forbid()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        env.User.Grant(Menu, PortalActions.View, PortalActions.Create);
        Assert.IsType<ForbidResult>(await Editar(env).OnGetAsync(id));
        env.User.Grant(Menu, PortalActions.Edit);
        var page = Editar(env);
        Assert.IsType<PageResult>(await page.OnGetAsync(id));
        Assert.Equal("Mueble", page.Form.Lines[0].Description);
    }

    [Fact]
    public async Task Importar_DevuelveLineasYErrores()
    {
        var env = ManifestTestEnv.Build();
        env.User.Grant(Menu, PortalActions.View, PortalActions.Create);
        var json = Assert.IsType<JsonResult>(await Editar(env).OnPostImportarAsync("A\tCaja\t2\t\t100\nB\t\t1", null));
        object? Prop(string n) => json.Value!.GetType().GetProperty(n)!.GetValue(json.Value);
        Assert.Single((System.Collections.IEnumerable)Prop("lines")!);
        Assert.Contains("Fila 2", string.Join(" ", ((IEnumerable<string>)Prop("errors")!)));
    }

    [Fact]
    public async Task Importar_Archivo_Xlsx()
    {
        var env = ManifestTestEnv.Build();
        env.User.Grant(Menu, PortalActions.View, PortalActions.Create);
        var bytes = Modulo.Despachos.Dominio.Guias.ManualGuideLineImporter.TemplateXlsx();
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "detalle.xlsx");
        var json = Assert.IsType<JsonResult>(await Editar(env).OnPostImportarAsync(null, file));
        Assert.NotNull(json.Value);
    }

    [Fact]
    public async Task Receptor_Json_DevuelveSocio()
    {
        var env = ManifestTestEnv.Build();
        env.User.Grant(Menu, PortalActions.View);
        env.Sap.Partners["76030680-0"] = new SapBusinessPartner("C76030680-0", "COMERCIAL DEPOR", "Venta", true,
            new[] { new SapPartnerAddress("PARQUE ARAUCO", "Kennedy 5413", "Las Condes", "Santiago") });
        var json = Assert.IsType<JsonResult>(await Editar(env).OnGetReceptorAsync("76.030.680-0"));
        Assert.Contains("C76030680-0", System.Text.Json.JsonSerializer.Serialize(json.Value));
    }

    [Fact]
    public async Task Detalle_Emitir_SinCreate_Forbid()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        env.User.Grant(Menu, PortalActions.View);
        var page = new Guias.DetalleModel(env.ManualGuides, env.User) { TempData = Temp() };
        Assert.IsType<PageResult>(await page.OnGetAsync(id));
        Assert.IsType<ForbidResult>(await page.OnPostEmitirAsync(id));

        env.User.Grant(Menu, PortalActions.Create);
        Assert.IsType<RedirectToPageResult>(await page.OnPostEmitirAsync(id));
        Assert.Equal(TransportGuideStatuses.Issued, (await env.ManualGuides.GetAsync(id))!.Status);
        Assert.IsAssignableFrom<FileContentResult>(await page.OnGetPdfAsync(id));
    }

    [Fact]
    public async Task Detalle_Anular_SinDelete_Forbid()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        env.User.Grant(Menu, PortalActions.View, PortalActions.Edit);
        var page = new Guias.DetalleModel(env.ManualGuides, env.User) { TempData = Temp() };
        Assert.IsType<ForbidResult>(await page.OnPostAnularAsync(id, "error"));
        env.User.Grant(Menu, PortalActions.Delete);
        Assert.IsType<RedirectToPageResult>(await page.OnPostAnularAsync(id, "error"));
        Assert.Equal(TransportGuideStatuses.Annulled, (await env.ManualGuides.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Detalle_Codigo_Png()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        env.User.Grant(Menu, PortalActions.View);
        var page = new Guias.DetalleModel(env.ManualGuides, env.User) { TempData = Temp() };
        Assert.Equal("image/png", Assert.IsType<FileContentResult>(await page.OnGetCodigoAsync(id)).ContentType);
    }

    [Fact]
    public async Task Motivos_Guardar_RequiereCreate()
    {
        var env = ManifestTestEnv.Build();
        env.User.Grant("Despachos.motivos-traslado", PortalActions.View);
        var page = new Motivos.IndexModel(env.ManualGuides, env.User) { TempData = Temp() };
        Assert.IsType<PageResult>(await page.OnGetAsync());
        Assert.Equal(5, page.Items.Count);
        page.Input = new Motivos.IndexModel.MotivoInput { Code = 8, Name = "Traslado para exportación", IsActive = true };
        Assert.IsType<ForbidResult>(await page.OnPostSaveAsync());
        env.User.Grant("Despachos.motivos-traslado", PortalActions.Create);
        Assert.IsType<RedirectToPageResult>(await page.OnPostSaveAsync());
        Assert.Equal(6, (await env.ManualGuides.ListMotivesAsync(includeInactive: true)).Count);
    }

    [Fact]
    public async Task Manifiesto_AgregarGuiaManual_RequiereEditar()
    {
        var env = ManifestTestEnv.Build();
        var gid = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        var mid = await env.Svc.CreateDraftAsync(env.Header());
        env.User.Grant("Despachos.manifiestos", PortalActions.View);
        var pdf = new ManifestPdfService(env.Db, env.Company, env.User, env.Svc, env.Clock);
        var page = new Manifiestos.DetalleModel(env.Svc, pdf, env.Master, env.User, new FakeContactLookup(), env.Cost, env.Guides,
            manualGuides: env.ManualGuides) { TempData = Temp() };

        Assert.IsType<ForbidResult>(await page.OnPostAgregarGuiaManualAsync(mid, gid));
        env.User.Grant("Despachos.manifiestos", PortalActions.Edit);
        Assert.IsType<RedirectToPageResult>(await page.OnPostAgregarGuiaManualAsync(mid, gid));
        Assert.Equal(DocumentKinds.ManualGuide, (await env.Svc.GetAsync(mid))!.Documents.Single().DocumentKind);
    }
}
