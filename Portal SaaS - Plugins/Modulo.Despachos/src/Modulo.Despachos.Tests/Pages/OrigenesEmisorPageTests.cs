using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;
using PortalSaas.Abstractions.Modelos;
using Origenes = Modulo.Despachos.Pages.Despachos.Configuracion.Origenes;

namespace Modulo.Despachos.Tests.Pages;

public class OrigenesEmisorPageTests
{
    private static (Origenes.IndexModel Page, MasterDataService Svc) Build(FakeCurrentUserContext user)
    {
        var svc = new MasterDataService(TestDb.Create(), new FakeCurrentCompanyAccessor(), user);
        return (new Origenes.IndexModel(svc, user), svc);
    }

    [Fact]
    public async Task SaveIssuer_SinPermisoEdit_Forbid_YNoGuarda()
    {
        var (page, svc) = Build(new FakeCurrentUserContext().Grant("Despachos.origenes", PortalActions.View, PortalActions.Create));
        page.IssuerInput = "76123456-0";
        Assert.IsType<ForbidResult>(await page.OnPostSaveIssuerAsync());
        Assert.Null(await svc.GetIssuerTaxIdAsync());
    }

    [Fact]
    public async Task SaveIssuer_ConEdit_GuardaRutNormalizado_YRechazaInvalido()
    {
        var (page, svc) = Build(new FakeCurrentUserContext().Grant("Despachos.origenes", PortalActions.View, PortalActions.Edit));
        page.IssuerInput = "76.123.456-0";
        await page.OnPostSaveIssuerAsync();
        Assert.Equal("76123456-0", await svc.GetIssuerTaxIdAsync());

        page.IssuerInput = "76123456-9";
        await page.OnPostSaveIssuerAsync();
        Assert.Equal("76123456-0", await svc.GetIssuerTaxIdAsync());
        Assert.NotNull(page.ErrorMessage);
    }
}
