using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
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
        return (new Origenes.IndexModel(svc, user, new FakeDteEmissionService()), svc);
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

    [Fact]
    public async Task SaveGuides_SinPermisoEdit_Forbid_YNoGuarda()
    {
        var (page, svc) = Build(new FakeCurrentUserContext().Grant("Despachos.origenes", PortalActions.View));
        page.GuidesInput = new Origenes.IndexModel.GuideSettingsInput { GuidesEnabled = true, TransferIndicator = 1, DispatchType = 2 };
        Assert.IsType<ForbidResult>(await page.OnPostSaveGuidesAsync());
        Assert.False((await svc.GetSettingsAsync())?.GuidesEnabled ?? false);
    }

    [Fact]
    public async Task SaveGuides_ConEdit_Guarda_YRechazaIndicadorInvalido()
    {
        var (page, svc) = Build(new FakeCurrentUserContext().Grant("Despachos.origenes", PortalActions.View, PortalActions.Edit));
        page.GuidesInput = new Origenes.IndexModel.GuideSettingsInput { GuidesEnabled = true, TransferIndicator = 5, DispatchType = 3, LineGrouping = GuideLineGroupings.Model, ItemCodePlacement = GuideItemCodePlacements.Name, MaxLines = 27 };
        await page.OnPostSaveGuidesAsync();
        var s = await svc.GetSettingsAsync();
        Assert.True(s!.GuidesEnabled);
        Assert.Equal((5, (int?)3), (s.TransferIndicator, s.DispatchType));
        Assert.Equal(GuideLineGroupings.Model, s.GuideLineGrouping);
        Assert.Equal(GuideItemCodePlacements.Name, s.GuideItemCodePlacement);
        Assert.Equal(27, s.GuideMaxLines);

        page.GuidesInput = new Origenes.IndexModel.GuideSettingsInput { GuidesEnabled = true, TransferIndicator = 12, DispatchType = 2 };
        await page.OnPostSaveGuidesAsync();
        Assert.NotNull(page.ErrorMessage);
        Assert.Equal(5, (await svc.GetSettingsAsync())!.TransferIndicator);

        page.GuidesInput = new Origenes.IndexModel.GuideSettingsInput { GuidesEnabled = true, TransferIndicator = 1, DispatchType = 2, MaxLines = 61 };
        await page.OnPostSaveGuidesAsync();
        Assert.Contains("entre 1 y 60", page.ErrorMessage);
        Assert.Equal(27, (await svc.GetSettingsAsync())!.GuideMaxLines);

        page.GuidesInput = new Origenes.IndexModel.GuideSettingsInput { GuidesEnabled = true, TransferIndicator = 1, DispatchType = 2, LineGrouping = "marca" };
        await page.OnPostSaveGuidesAsync();
        Assert.NotNull(page.ErrorMessage);
        Assert.Equal(GuideLineGroupings.Model, (await svc.GetSettingsAsync())!.GuideLineGrouping);
    }

    [Fact]
    public async Task OnGet_MuestraEstadoDeLaConfiguracionDte()
    {
        var (page, _) = Build(new FakeCurrentUserContext().Grant("Despachos.origenes", PortalActions.View));
        await page.OnGetAsync();
        Assert.True(page.DteStatus!.IsConfigured);
        Assert.Equal("FAKE", page.DteStatus.ProviderCode);
    }
}
