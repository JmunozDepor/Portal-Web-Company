using Modulo.Despachos.Data;
using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Dominio.Tarifas;
using Modulo.Despachos.Tests.Fakes;

namespace Modulo.Despachos.Tests.Servicios;

public class TariffServiceTests
{
    private static readonly DateOnly Valid = new(2026, 1, 1);

    private sealed class Env
    {
        public required TariffService Svc { get; init; }
        public required MasterDataService Master { get; init; }
        public required DespachosDbContext Db { get; init; }
        public required FakeCurrentCompanyAccessor Company { get; init; }
    }

    private static Env Build()
    {
        var db = TestDb.Create();
        var company = new FakeCurrentCompanyAccessor();
        var user = new FakeCurrentUserContext();
        var engine = TarifasTestData.Engine();
        var calculators = new IRateCalculator[]
        {
            new FixedPerTripCalculator(), new PerM3Calculator(), new PerKmCalculator(),
            new PerPackageCalculator(), new PerDocumentCalculator(), new NoChargeCalculator()
        };
        var conditions = new ISurchargeCondition[]
        {
            new DistinctZonesOverCondition(), new DistinctCommunesOverCondition(), new DepartureTimeFromCondition(),
            new ShiftIsCondition(), new WeekdayInCondition(), new IsHolidayCondition(),
            new DocumentsOverCondition(), new PackagesOverCondition(), new VehicleTypeIsCondition()
        };
        return new Env
        {
            Svc = new TariffService(db, company, user, engine, calculators, conditions),
            Master = new MasterDataService(db, company, user),
            Db = db,
            Company = company,
        };
    }

    private static RateSurcharge Sur(string type, string? json, string effect = "fixed_amount", decimal value = 1000) =>
        new() { Name = "R", ConditionType = type, ConditionParametersJson = json, EffectType = effect, Value = value, ValidFrom = Valid };

    [Theory]
    [InlineData("departure_time_from", "{\"time\":\"25:00\"}")]
    [InlineData("departure_time_from", "{}")]
    [InlineData("distinct_zones_over", "{}")]
    [InlineData("documents_over", "{\"threshold\":\"x\"}")]
    [InlineData("packages_over", null)]
    [InlineData("weekday_in", "{\"days\":[9]}")]
    [InlineData("shift_is", "{}")]
    [InlineData("no_existe", "{}")]
    public async Task SaveSurcharge_ValidaParametrosSegunTipo(string type, string? json)
    {
        var env = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.SaveSurchargeAsync(Sur(type, json)));
        Assert.Empty(await env.Svc.ListSurchargesAsync());
    }

    [Fact]
    public async Task SaveSurcharge_ParametrosValidos_Guarda()
    {
        var env = Build();
        await env.Svc.SaveSurchargeAsync(Sur("departure_time_from", "{\"time\":\"17:00\"}", "percent_of_base", 20));
        await env.Svc.SaveSurchargeAsync(Sur("distinct_zones_over", "{\"threshold\":1}"));
        await env.Svc.SaveSurchargeAsync(Sur("is_holiday", null));
        Assert.Equal(3, (await env.Svc.ListSurchargesAsync()).Count);
    }

    [Theory]
    [InlineData("fixed_amount", 0)]
    [InlineData("fixed_amount", -5)]
    [InlineData("percent_of_base", 1001)]
    [InlineData("otro_efecto", 10)]
    public async Task SaveSurcharge_RechazaEfectoOValorInvalido(string effect, double value)
    {
        var env = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.SaveSurchargeAsync(Sur("is_holiday", null, effect, (decimal)value)));
    }

    [Fact]
    public async Task SaveRateCard_RechazaMetodoDesconocido()
    {
        var env = Build();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.SaveRateCardAsync(new RateCard { CalculationMethod = "per_pallet", ValidFrom = Valid, Amount = 1 }));
        Assert.Contains("per_pallet", ex.Message);
    }

    [Fact]
    public async Task SaveRateCard_RechazaRangoM3Invertido()
    {
        var env = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.SaveRateCardAsync(new RateCard { ValidFrom = Valid, Amount = 1, M3From = 20, M3To = 10 }));
    }

    [Fact]
    public async Task SaveRateCard_RechazaRegionFueraDelCatalogo()
    {
        var env = Build();
        await env.Master.ListCommunesAsync(); // siembra
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.SaveRateCardAsync(new RateCard { ValidFrom = Valid, Amount = 1, RegionCode = "99" }));
        var id = await env.Svc.SaveRateCardAsync(new RateCard { ValidFrom = Valid, Amount = 1, RegionCode = "06" });
        Assert.True(id > 0);
    }

    [Fact]
    public async Task Simulate_UsaCapacidadDelTipoSiVehiculoNoLaDefine()
    {
        var env = Build();
        var communes = await env.Master.ListCommunesAsync();
        var rancagua = communes.First(c => c.Name == "Rancagua");
        var tipo = await env.Master.SaveVehicleTypeAsync(new VehicleType { Code = "CAM", Name = "Camión", CapacityM3 = 30m });
        var veh = await env.Master.SaveVehicleAsync(new Vehicle { Plate = "ABCD12", VehicleTypeId = tipo });
        await env.Svc.SaveRateCardAsync(new RateCard
            { ValidFrom = Valid, CalculationMethod = "per_m3", Amount = 1000, RegionCode = rancagua.RegionCode });

        var input = new SimulationInput(1, null, veh, new DateOnly(2026, 11, 10), new TimeOnly(10, 0),
            new[] { rancagua.Id }, 5, 5, 10, null);
        var trip = await env.Svc.BuildTripAsync(input);
        var result = await env.Svc.SimulateAsync(input);

        Assert.Equal(30m, trip.CapacityM3);
        Assert.Equal("ok", result.Status);
        Assert.Equal(30000m, result.Total);
    }

    [Fact]
    public async Task Simulate_CapacidadPropiaDelVehiculoTienePrioridad()
    {
        var env = Build();
        var communes = await env.Master.ListCommunesAsync();
        var tipo = await env.Master.SaveVehicleTypeAsync(new VehicleType { Code = "CAM", Name = "Camión", CapacityM3 = 30m });
        var veh = await env.Master.SaveVehicleAsync(new Vehicle { Plate = "ABCD12", VehicleTypeId = tipo, CapacityM3 = 12m });

        var trip = await env.Svc.BuildTripAsync(new SimulationInput(1, null, veh, new DateOnly(2026, 11, 10),
            new TimeOnly(10, 0), new[] { communes[0].Id }, 1, 1, 1, null));

        Assert.Equal(12m, trip.CapacityM3);
        Assert.Equal(tipo, trip.VehicleTypeId);
    }

    [Fact]
    public async Task Simulate_MarcaFeriado()
    {
        var env = Build();
        var communes = await env.Master.ListCommunesAsync();
        var veh = await env.Master.SaveVehicleAsync(new Vehicle { Plate = "ABCD12" });
        await env.Master.SaveHolidayAsync(new Holiday { Date = new DateOnly(2026, 12, 8), Name = "Inmaculada Concepción" });

        SimulationInput On(DateOnly d) => new(1, null, veh, d, new TimeOnly(10, 0), new[] { communes[0].Id }, 1, 1, 1, null);

        Assert.True((await env.Svc.BuildTripAsync(On(new DateOnly(2026, 12, 8)))).IsHoliday);
        Assert.False((await env.Svc.BuildTripAsync(On(new DateOnly(2026, 12, 9)))).IsHoliday);
    }

    [Fact]
    public async Task BuildTrip_DerivaZonaUnicaDeCadaComuna()
    {
        var env = Build();
        var communes = await env.Master.ListCommunesAsync();
        var veh = await env.Master.SaveVehicleAsync(new Vehicle { Plate = "ABCD12" });
        var zoneId = await env.Master.SaveZoneAsync(new Zone
            { Code = "N", Name = "Norte", Communes = { new ZoneCommune { CommuneId = communes[0].Id } } });

        var trip = await env.Svc.BuildTripAsync(new SimulationInput(1, null, veh, new DateOnly(2026, 11, 10),
            new TimeOnly(10, 0), new[] { communes[0].Id, communes[1].Id }, 1, 1, 1, null));

        Assert.Equal(zoneId, trip.Localities.Single(l => l.CommuneId == communes[0].Id).ZoneId);
        Assert.Null(trip.Localities.Single(l => l.CommuneId == communes[1].Id).ZoneId);
        Assert.Equal(communes[0].RegionCode, trip.Localities[0].RegionCode);
    }

    [Fact]
    public async Task DeleteZone_ConTarifas_Bloquea()
    {
        var env = Build();
        var communes = await env.Master.ListCommunesAsync();
        var zoneId = await env.Master.SaveZoneAsync(new Zone
            { Code = "N", Name = "Norte", Communes = { new ZoneCommune { CommuneId = communes[0].Id } } });
        await env.Svc.SaveRateCardAsync(new RateCard { ValidFrom = Valid, Amount = 1, ZoneId = zoneId });

        var result = await env.Master.DeleteZoneAsync(zoneId);

        Assert.False(result.Deleted);
        Assert.Contains("tarifas", result.BlockedReason);
    }
}
