using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;
using PortalSaas.Abstractions.Modelos;
using Origenes = Modulo.Despachos.Pages.Despachos.Configuracion.Origenes;

namespace Modulo.Despachos.Tests.Pages;

public class OrigenesEmisorPageTests
{
    private static (Origenes.IndexModel Page, MasterDataService Svc) Build(FakeCurrentUserContext user) => Build(user, out _);

    private static (Origenes.IndexModel Page, MasterDataService Svc) Build(FakeCurrentUserContext user, out FakeSapTransferWriter writer)
    {
        var db = TestDb.Create();
        var company = new FakeCurrentCompanyAccessor();
        var svc = new MasterDataService(db, company, user);
        writer = new FakeSapTransferWriter();
        var guides = new TransportGuideService(db, company, user, new FakeSapDocumentLookupService(), new FakeDteEmissionService(),
            TimeProvider.System, new FakeNotificationService(), writer: writer);
        return (new Origenes.IndexModel(svc, user, new FakeDteEmissionService(), guides), svc);
    }

    [Fact]
    public async Task ProbarEscritura_RequiereEditar_YMuestraElResultado()
    {
        var (page, svc) = Build(new FakeCurrentUserContext().Grant("Despachos.origenes", PortalActions.View), out var writer);
        await svc.SaveTransferSettingsAsync(null, "52", null, null, 5, 3);
        Assert.IsType<ForbidResult>(await page.OnPostTestSapWriteAsync(777));

        var (page2, svc2) = Build(new FakeCurrentUserContext().Grant("Despachos.origenes", PortalActions.View, PortalActions.Edit), out var writer2);
        await svc2.SaveTransferSettingsAsync(null, "52", null, null, 5, 3);
        await page2.OnPostTestSapWriteAsync(777);
        Assert.Contains("SAP aceptó", page2.SuccessMessage);
        Assert.Equal(777, Assert.Single(writer2.Writes).DocEntry);

        writer2.FailWith = "Documento cerrado";
        await page2.OnPostTestSapWriteAsync(777);
        Assert.Contains("SAP rechazó", page2.ErrorMessage);
        Assert.Empty(writer.Writes);
    }

    private static Origenes.IndexModel.TransferSettingsInput Traslados(string? field = "U_NX_Manifiesto", string? pattern = @"^(CTBO|ICBO)\d+$") => new()
    {
        LpnPattern = pattern, FolioPrefix = "52", ManifestField = field, DispatchDateField = "U_NX_FechaDesp",
        InternalTransferIndicator = 5, InternalDispatchType = 3,
    };

    [Fact]
    public async Task SaveTransfers_ConEdit_Guarda()
    {
        var (page, svc) = Build(new FakeCurrentUserContext().Grant("Despachos.origenes", PortalActions.View, PortalActions.Edit));
        page.TransfersInput = Traslados();

        await page.OnPostSaveTransfersAsync();

        var s = (await svc.GetSettingsAsync())!;
        Assert.Null(page.ErrorMessage);
        Assert.Equal((@"^(CTBO|ICBO)\d+$", "52", "U_NX_Manifiesto", "U_NX_FechaDesp", 5, (int?)3),
            (s.LpnPattern, s.TransferFolioPrefix, s.TransferManifestField, s.TransferDispatchDateField, s.InternalTransferIndicator, s.InternalDispatchType));
    }

    [Theory]
    [InlineData("U_X; DROP")]
    [InlineData("Manifiesto")]
    public async Task SaveTransfers_CampoInvalido_Rechaza(string field)
    {
        var (page, svc) = Build(new FakeCurrentUserContext().Grant("Despachos.origenes", PortalActions.View, PortalActions.Edit));
        page.TransfersInput = Traslados(field: field);

        await page.OnPostSaveTransfersAsync();

        Assert.Contains("no es válido", page.ErrorMessage);
        Assert.Null((await svc.GetSettingsAsync())?.TransferManifestField);
    }

    [Fact]
    public async Task SaveTransfers_PatronInvalido_Rechaza()
    {
        var (page, _) = Build(new FakeCurrentUserContext().Grant("Despachos.origenes", PortalActions.View, PortalActions.Edit));
        page.TransfersInput = Traslados(pattern: "((");
        await page.OnPostSaveTransfersAsync();
        Assert.Contains("patrón", page.ErrorMessage);
    }

    [Fact]
    public async Task SaveTransfers_PrefijoVacio_SeGuardaVacio()
    {
        var (page, svc) = Build(new FakeCurrentUserContext().Grant("Despachos.origenes", PortalActions.View, PortalActions.Edit));
        page.TransfersInput = Traslados() with { FolioPrefix = "  " };
        await page.OnPostSaveTransfersAsync();
        Assert.Equal("", (await svc.GetSettingsAsync())!.TransferFolioPrefix);
    }

    [Fact]
    public async Task SaveTransfers_SinPermisoEdit_Forbid()
    {
        var (page, svc) = Build(new FakeCurrentUserContext().Grant("Despachos.origenes", PortalActions.View));
        page.TransfersInput = Traslados();
        Assert.IsType<ForbidResult>(await page.OnPostSaveTransfersAsync());
        Assert.Null((await svc.GetSettingsAsync())?.LpnPattern);
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
        page.GuidesInput = new Origenes.IndexModel.GuideSettingsInput { GuidesEnabled = true, TransferIndicator = 5, DispatchType = 3, LineGrouping = GuideLineGroupings.Model, ItemCodePlacement = GuideItemCodePlacements.Name, MaxLines = 27, Valued = true };
        await page.OnPostSaveGuidesAsync();
        var s = await svc.GetSettingsAsync();
        Assert.True(s!.GuidesEnabled);
        Assert.Equal((5, (int?)3), (s.TransferIndicator, s.DispatchType));
        Assert.Equal(GuideLineGroupings.Model, s.GuideLineGrouping);
        Assert.Equal(GuideItemCodePlacements.Name, s.GuideItemCodePlacement);
        Assert.Equal(27, s.GuideMaxLines);
        Assert.True(s.GuideValued);

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
