using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Dominio.Tarifas;
using Modulo.Despachos.Tests.Fakes;
using PortalSaas.Abstractions.Modelos;
using Tarifario = Modulo.Despachos.Pages.Despachos.Tarifario;

namespace Modulo.Despachos.Tests.Pages;

public class TarifarioPageTests
{
    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static (Tarifario.IndexModel Page, TariffService Tariff) Build(FakeCurrentUserContext user)
    {
        var db = TestDb.Create();
        var company = new FakeCurrentCompanyAccessor();
        var master = new MasterDataService(db, company, user);
        var tariff = new TariffService(db, company, user, TarifasTestData.Engine(),
            new IRateCalculator[] { new FixedPerTripCalculator(), new NoChargeCalculator() },
            new ISurchargeCondition[] { new DepartureTimeFromCondition(), new DistinctZonesOverCondition(), new IsHolidayCondition() });
        var page = new Tarifario.IndexModel(tariff, master, user)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider()),
        };
        return (page, tariff);
    }

    [Fact]
    public async Task OnGet_SinView_Forbid_YSimularTambien()
    {
        var (page, _) = Build(new FakeCurrentUserContext());
        Assert.IsType<ForbidResult>(await page.OnGetAsync());
        Assert.IsType<ForbidResult>(await page.OnPostSimulateAsync());
    }

    [Fact]
    public async Task Guardar_y_Eliminar_ExigenPermisoPropio()
    {
        var user = new FakeCurrentUserContext().Grant("Despachos.tarifario", PortalActions.View, PortalActions.Edit);
        var (page, _) = Build(user);
        page.Card = new Tarifario.IndexModel.CardInput { Id = 0 };
        Assert.IsType<ForbidResult>(await page.OnPostSaveCardAsync());      // crear exige CREATE
        page.Surcharge = new Tarifario.IndexModel.SurchargeInput { Id = 0 };
        Assert.IsType<ForbidResult>(await page.OnPostSaveSurchargeAsync()); // idem
        Assert.IsType<ForbidResult>(await page.OnPostDeleteCardAsync(1));   // eliminar exige DELETE
        Assert.IsType<ForbidResult>(await page.OnPostDeleteSurchargeAsync(1));
    }

    [Fact]
    public async Task GuardarRecargo_ArmaElJsonDeParametrosDesdeCamposTipados()
    {
        var user = new FakeCurrentUserContext().Grant("Despachos.tarifario", PortalActions.View, PortalActions.Create);
        var (page, tariff) = Build(user);
        page.Surcharge = new Tarifario.IndexModel.SurchargeInput
        {
            Name = "Salida tarde", ValidFrom = "2026-01-01", IsActive = true,
            ConditionType = "departure_time_from", CondTime = "17:00",
            EffectType = "percent_of_base", Value = "20.5",
        };

        Assert.IsType<RedirectToPageResult>(await page.OnPostSaveSurchargeAsync());

        var saved = (await tariff.ListSurchargesAsync()).Single();
        Assert.Equal("{\"time\":\"17:00\"}", saved.ConditionParametersJson);
        Assert.Equal(20.5m, saved.Value); // decimal con punto, cultura invariante
    }

    [Fact]
    public async Task GuardarRecargo_HoraInvalida_NoGuarda_YAvisa()
    {
        var user = new FakeCurrentUserContext().Grant("Despachos.tarifario", PortalActions.View, PortalActions.Create);
        var (page, tariff) = Build(user);
        page.Surcharge = new Tarifario.IndexModel.SurchargeInput
        {
            Name = "X", ValidFrom = "2026-01-01", ConditionType = "departure_time_from", CondTime = "25:00",
            EffectType = "fixed_amount", Value = "1000",
        };

        await page.OnPostSaveSurchargeAsync();

        Assert.Empty(await tariff.ListSurchargesAsync());
        Assert.NotNull(page.ErrorMessage);
    }
}
