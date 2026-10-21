using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;
using PortalSaas.Abstractions.Modelos;
using Transportistas = Modulo.Despachos.Pages.Despachos.Configuracion.Transportistas;

namespace Modulo.Despachos.Tests.Pages;

public class MaestrosPermisosTests
{
    private static Transportistas.IndexModel Build(FakeCurrentUserContext user)
    {
        var svc = new MasterDataService(TestDb.Create(), new FakeCurrentCompanyAccessor(), user);
        return new Transportistas.IndexModel(svc, user);
    }

    [Fact]
    public async Task OnGet_SinPermisoView_Forbid()
    {
        var page = Build(new FakeCurrentUserContext());
        Assert.IsType<ForbidResult>(await page.OnGetAsync());
    }

    [Fact]
    public async Task OnPostDelete_SinPermisoDelete_Forbid()
    {
        // Tiene VIEW y EDIT pero no DELETE: igual debe rechazar.
        var user = new FakeCurrentUserContext().Grant("Despachos.transportistas", PortalActions.View, PortalActions.Edit);
        var page = Build(user);
        Assert.IsType<ForbidResult>(await page.OnPostDeleteAsync(1));
    }

    [Fact]
    public async Task OnPostSave_NuevoSinCreate_Forbid_YEditSinEdit_Forbid()
    {
        var user = new FakeCurrentUserContext().Grant("Despachos.transportistas", PortalActions.View, PortalActions.Edit);
        var page = Build(user);
        page.Input = new Transportistas.IndexModel.TransportistaInput { Id = 0, Code = "A", Name = "A" };
        Assert.IsType<ForbidResult>(await page.OnPostSaveAsync());

        var sinEdit = Build(new FakeCurrentUserContext().Grant("Despachos.transportistas", PortalActions.Create));
        sinEdit.Input = new Transportistas.IndexModel.TransportistaInput { Id = 5, Code = "A", Name = "A" };
        Assert.IsType<ForbidResult>(await sinEdit.OnPostSaveAsync());
    }
}
