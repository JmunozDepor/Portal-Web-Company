using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;
using Modulo.Despachos.Tests.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Manifiestos = Modulo.Despachos.Pages.Despachos.Manifiestos;
using Retorno = Modulo.Despachos.Pages.Despachos.Retorno;

namespace Modulo.Despachos.Tests.Pages;

/// <summary>Gates de permisos y avisos de las pantallas de manifiesto y retorno.</summary>
public class ManifiestosPermisosTests
{
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

    private static (Manifiestos.DetalleModel Page, ManifestTestEnv Env) BuildDetalle(Action<FakeCurrentUserContext>? grant = null)
    {
        var env = ManifestTestEnv.Build();
        grant?.Invoke(env.User);
        var pdf = new ManifestPdfService(env.Db, env.Company, env.User, env.Svc, env.Clock);
        var page = new Manifiestos.DetalleModel(env.Svc, pdf, env.Master, env.User, new FakeContactLookup(), env.Cost, env.Guides) { TempData = Temp() };
        return (page, env);
    }

    [Fact]
    public async Task Guias_BorradorPdf_RequiereVer()
    {
        var (page, env) = BuildDetalle();
        await env.EnableGuidesAsync();
        var id = await env.GuideReadyDraftAsync(1);
        Assert.IsType<ForbidResult>(await page.OnGetGuiaBorradorAsync(id, 1));

        env.User.Grant("Despachos.manifiestos", PortalActions.View);
        Assert.IsType<Manifiestos.InlinePdfResult>(await page.OnGetGuiaBorradorAsync(id, 1));
    }

    [Fact]
    public async Task Guias_Pdf_RequiereVer_YDevuelveGuiasEmitidas()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.Approve));
        await env.EnableGuidesAsync();
        var id = await env.GuideReadyDraftAsync(1);
        Assert.True((await env.Svc.DispatchAsync(id)).Success);
        Assert.IsType<ForbidResult>(await page.OnGetGuiasPdfAsync(id, null));

        env.User.Grant("Despachos.manifiestos", PortalActions.View);
        Assert.IsType<Manifiestos.InlinePdfResult>(await page.OnGetGuiasPdfAsync(id, null));
    }

    [Fact]
    public async Task Guias_Reintentar_RequiereDespachar()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Edit));
        await env.EnableGuidesAsync();
        var id = await env.GuideReadyDraftAsync(1);
        env.Dte.Down = true;
        await env.Svc.DispatchAsync(id);
        env.Dte.Down = false;
        Assert.IsType<ForbidResult>(await page.OnPostReintentarGuiasAsync(id));
        Assert.Equal(ManifestStatuses.PendingGuides, (await env.Svc.GetAsync(id))!.Status);

        env.User.Grant("Despachos.manifiestos", PortalActions.Approve);
        Assert.IsType<RedirectToPageResult>(await page.OnPostReintentarGuiasAsync(id));
        Assert.Equal(ManifestStatuses.Dispatched, (await env.Svc.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Detalle_ConGuias_CargaVistaPreviaEnBorrador_YGuiasTrasDespachar()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Approve));
        await env.EnableGuidesAsync();
        var id = await env.GuideReadyDraftAsync(1);

        Assert.IsType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>(await page.OnGetAsync(id));
        Assert.True(page.GuidesEnabled);
        Assert.Single(page.GuidePreview!.Guides);

        await env.Svc.DispatchAsync(id);
        Assert.IsType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>(await page.OnGetAsync(id));
        Assert.Equal(500, Assert.Single(page.Guides).Folio);
    }

    [Fact]
    public async Task Despachar_SinApprove_Forbid()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Edit));
        var id = await env.DraftWithDocsAsync(100);
        Assert.IsType<ForbidResult>(await page.OnPostDespacharAsync(id));
        Assert.Equal(ManifestStatuses.Draft, (await env.Svc.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Despachar_ConApprove_Despacha()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Approve));
        var id = await env.DraftWithDocsAsync(100);
        Assert.IsType<RedirectToPageResult>(await page.OnPostDespacharAsync(id));
        Assert.Equal(ManifestStatuses.Dispatched, (await env.Svc.GetAsync(id))!.Status);
        Assert.NotNull(page.SuccessMessage);
    }

    [Fact]
    public async Task Anular_SinDelete_Forbid()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Edit, PortalActions.Approve));
        var id = await env.DraftWithDocsAsync(100);
        Assert.IsType<ForbidResult>(await page.OnPostAnularAsync(id, "error"));
        Assert.Equal(ManifestStatuses.Draft, (await env.Svc.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Anular_ConDelete_Anula()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Delete));
        var id = await env.DraftWithDocsAsync(100);
        Assert.IsType<RedirectToPageResult>(await page.OnPostAnularAsync(id, "error de armado"));
        Assert.Equal(ManifestStatuses.Annulled, (await env.Svc.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Pdf_SinExport_Forbid()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View));
        var id = await env.DraftWithDocsAsync(100);
        Assert.IsType<ForbidResult>(await page.OnGetPdfAsync(id));
    }

    [Fact]
    public async Task Pdf_ConExport_DevuelveArchivo_YLaSegundaImpresionEsCopia()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Export, PortalActions.Approve));
        var id = await env.DraftWithDocsAsync(100);
        await page.OnPostDespacharAsync(id);

        var first = Assert.IsAssignableFrom<FileContentResult>(await page.OnGetPdfAsync(id));
        Assert.Equal("application/pdf", first.ContentType);
        Assert.StartsWith("manifiesto-", first.FileDownloadName);
        Assert.Contains(env.Db.ManifestEvents, e => e.ManifestId == id && e.EventType == ManifestEventTypes.Printed);

        await page.OnGetPdfAsync(id);
        Assert.Contains(env.Db.ManifestEvents, e => e.ManifestId == id && e.EventType == ManifestEventTypes.Reprinted);
    }

    [Fact]
    public async Task AjustarCosto_SinEditEnCierreMensual_Forbid()
    {
        // EDIT en manifiestos NO basta: el ajuste de costo exige EDIT en cierre-mensual.
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Edit, PortalActions.Approve));
        var id = await env.DraftWithDocsAsync(100);
        await env.Svc.DispatchAsync(id);
        Assert.IsType<ForbidResult>(await page.OnPostAjustarCostoAsync(id, "1000", "motivo"));
        Assert.Null((await env.Svc.GetAsync(id))!.CostOverride);
    }

    [Fact]
    public async Task AjustarCosto_ConEditEnCierreMensual_Ajusta()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.cierre-mensual", PortalActions.Edit));
        var id = await env.DraftWithDocsAsync(100);
        await env.Svc.DispatchAsync(id);
        Assert.IsType<RedirectToPageResult>(await page.OnPostAjustarCostoAsync(id, "1234.5", "peaje"));
        Assert.Equal(1234.5m, (await env.Svc.GetAsync(id))!.CostOverride);
    }

    [Fact]
    public async Task RecalcularCosto_SinEdit_Forbid_ConEdit_Recalcula()
    {
        var (viewer, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Approve));
        var id = await env.DraftWithDocsAsync(100);
        await env.Svc.DispatchAsync(id);
        Assert.IsType<ForbidResult>(await viewer.OnPostRecalcularCostoAsync(id));

        env.User.Grant("Despachos.manifiestos", PortalActions.Edit);
        Assert.IsType<RedirectToPageResult>(await viewer.OnPostRecalcularCostoAsync(id));
        Assert.Contains(env.Db.ManifestEvents, e => e.ManifestId == id && e.EventType == ManifestEventTypes.CostRecalculated);
    }

    [Fact]
    public async Task CorregirComuna_SinEdit_Forbid_ConEdit_Corrige()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Approve));
        env.AddSap(300, commune: "Villa Nowhere");
        var id = await env.DraftWithDocsAsync(300);
        await env.Svc.DispatchAsync(id);
        var docId = (await env.Svc.GetAsync(id))!.Documents.Single().Id;
        Assert.IsType<ForbidResult>(await page.OnPostCorregirComunaAsync(id, docId, env.CuricoId));

        env.User.Grant("Despachos.manifiestos", PortalActions.Edit);
        Assert.IsType<RedirectToPageResult>(await page.OnPostCorregirComunaAsync(id, docId, env.CuricoId));
        Assert.Equal(env.CuricoId, (await env.Svc.GetAsync(id))!.Documents.Single().DestinationCommuneId);
    }

    [Fact]
    public async Task AgregarDocumento_SinEdit_Forbid()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View));
        var id = await env.DraftWithDocsAsync(100);
        Assert.IsType<ForbidResult>(await page.OnPostAgregarDocumentoAsync(id, new Manifiestos.DetalleModel.ScanInput { Folio = 5 }));
    }

    [Fact]
    public async Task AgregarDocumento_ConEdit_DevuelveJsonConOutcomeYContadores()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Edit));
        var id = await env.DraftWithDocsAsync(100);
        env.AddSap(200);
        var json = Assert.IsType<JsonResult>(await page.OnPostAgregarDocumentoAsync(id,
            new Manifiestos.DetalleModel.ScanInput { DocumentKind = DocumentKinds.Invoice, Folio = 200 }));
        var text = System.Text.Json.JsonSerializer.Serialize(json.Value);
        Assert.Contains("\"outcome\":\"added\"", text);
        Assert.Contains("\"totalDocuments\":2", text);
    }

    [Fact]
    public async Task Cerrar_SinApprove_Forbid()
    {
        var (page, _) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Edit));
        Assert.IsType<ForbidResult>(await page.OnPostCerrarAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Detalle_SinView_Forbid()
    {
        var (page, _) = BuildDetalle();
        Assert.IsType<ForbidResult>(await page.OnGetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Nuevo_SinCreate_Forbid()
    {
        var env = ManifestTestEnv.Build();
        env.User.Grant("Despachos.manifiestos", PortalActions.View);
        var page = new Manifiestos.NuevoModel(env.Svc, env.Master, env.User) { TempData = Temp() };
        Assert.IsType<ForbidResult>(await page.OnGetAsync());
        Assert.IsType<ForbidResult>(await page.OnPostAsync());
    }

    [Fact]
    public async Task RegistrarRetorno_SinEditEnRetorno_Forbid()
    {
        var env = ManifestTestEnv.Build();
        // EDIT en manifiestos no basta: el retorno es su propio menú.
        env.User.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Edit)
                .Grant("Despachos.retorno", PortalActions.View);
        var page = new Retorno.IndexModel(env.Svc, env.Master, env.User) { TempData = Temp() };
        var id = await env.DraftWithDocsAsync(100);
        await env.Svc.DispatchAsync(id);
        Assert.IsType<ForbidResult>(await page.OnPostRegistrarAsync(id, null, new List<Retorno.IndexModel.RowInput>()));
    }

    [Fact]
    public async Task Retorno_SinView_Forbid()
    {
        var env = ManifestTestEnv.Build();
        var page = new Retorno.IndexModel(env.Svc, env.Master, env.User) { TempData = Temp() };
        Assert.IsType<ForbidResult>(await page.OnGetAsync(null));
    }

    [Fact]
    public async Task Retorno_ConEdit_RegistraEntregados_YPasaARetornado()
    {
        var env = ManifestTestEnv.Build();
        env.User.Grant("Despachos.retorno", PortalActions.View, PortalActions.Edit);
        var page = new Retorno.IndexModel(env.Svc, env.Master, env.User) { TempData = Temp() };
        var id = await env.DraftWithDocsAsync(100, 101);
        await env.Svc.DispatchAsync(id);
        var docs = (await env.Svc.GetAsync(id))!.Documents;
        var rows = docs.Select(d => new Retorno.IndexModel.RowInput { DocumentId = d.Id, Status = DeliveryStatuses.Delivered }).ToList();

        Assert.IsType<RedirectToPageResult>(await page.OnPostRegistrarAsync(id, null, rows));
        Assert.Equal(ManifestStatuses.Returned, (await env.Svc.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Retorno_RechazoSinMotivo_NoRegistra_YAvisa()
    {
        var env = ManifestTestEnv.Build();
        env.User.Grant("Despachos.retorno", PortalActions.View, PortalActions.Edit);
        var page = new Retorno.IndexModel(env.Svc, env.Master, env.User) { TempData = Temp() };
        var id = await env.DraftWithDocsAsync(100);
        await env.Svc.DispatchAsync(id);
        var doc = (await env.Svc.GetAsync(id))!.Documents.Single();

        await page.OnPostRegistrarAsync(id, null, new List<Retorno.IndexModel.RowInput>
        {
            new() { DocumentId = doc.Id, Status = DeliveryStatuses.Rejected },
        });

        Assert.NotNull(page.ErrorMessage);
        Assert.Equal(ManifestStatuses.Dispatched, (await env.Svc.GetAsync(id))!.Status);
    }

    /// <summary>ITenantUserAdminService mínimo: solo ListAsync responde (lo único que usa la pantalla).</summary>
    private class UsersProxy : DispatchProxy
    {
        public IReadOnlyList<TenantUserDto> Users { get; set; } = Array.Empty<TenantUserDto>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod!.Name == nameof(ITenantUserAdminService.ListAsync)
                ? Task.FromResult(Users)
                : throw new NotImplementedException(targetMethod.Name);
    }

    [Fact]
    public async Task Destinatarios_UsuarioAjenoAlTenant_NoSeGuarda()
    {
        var env = ManifestTestEnv.Build();
        env.User.Grant("Despachos.destinatarios-avisos", PortalActions.View, PortalActions.Create);
        var own = new TenantUserDto { Id = Guid.NewGuid(), Username = "ana", Email = "a@x.cl", IsAdmin = false, IsActive = true, IsLocked = false };
        var proxy = DispatchProxy.Create<ITenantUserAdminService, UsersProxy>();
        ((UsersProxy)(object)proxy).Users = new[] { own };
        var page = new Modulo.Despachos.Pages.Despachos.Configuracion.DestinatariosAvisos.IndexModel(env.Master, proxy, env.User) { TempData = Temp() };

        page.Input = new() { UserId = Guid.NewGuid(), IsActive = true };
        await page.OnPostSaveAsync();
        Assert.NotNull(page.ErrorMessage);
        Assert.Empty(await env.Master.ListNotificationRecipientsAsync(includeInactive: true));

        page.ErrorMessage = null;
        page.Input = new() { UserId = own.Id, IsActive = true };
        await page.OnPostSaveAsync();
        Assert.Null(page.ErrorMessage);
        Assert.Single(await env.Master.ListNotificationRecipientsAsync(includeInactive: true));
    }

    [Fact]
    public async Task Zonas_MensajesConGeneroFemenino()
    {
        var env = ManifestTestEnv.Build();
        env.User.Grant("Despachos.zonas", PortalActions.View, PortalActions.Create, PortalActions.Delete);
        var page = new Modulo.Despachos.Pages.Despachos.Configuracion.Zonas.IndexModel(env.Master, env.User) { TempData = Temp() };
        page.Input = new() { Code = "Z1", Name = "Zona 1", IsActive = true, CommuneIds = new() { env.RancaguaId } };
        await page.OnPostSaveAsync();
        Assert.Equal("Zona creada.", page.SuccessMessage);
        var id = (await env.Master.ListZonesAsync(includeInactive: true)).Single().Id;
        await page.OnPostDeleteAsync(id);
        Assert.Equal("Zona eliminada.", page.SuccessMessage);
    }

    [Fact]
    public async Task Listado_SinView_Forbid()
    {
        var env = ManifestTestEnv.Build();
        var page = new Manifiestos.IndexModel(env.Svc, env.Master, env.User) { TempData = Temp() };
        Assert.IsType<ForbidResult>(await page.OnGetAsync());
    }

    [Fact]
    public async Task Detalle_GuiasPendientes_PermiteAnular()
    {
        var (page, env) = BuildDetalle(u => u.Grant("Despachos.manifiestos", PortalActions.View, PortalActions.Delete));
        await env.EnableGuidesAsync();
        var id = await env.GuideReadyDraftAsync(1);
        env.Dte.Down = true;
        await env.Svc.DispatchAsync(id);

        await page.OnGetAsync(id);

        Assert.True(page.CanAnnulNow);
        Assert.True(page.ShowGuides);
    }
}
