using System.Text.Json;
using Modulo.Despachos.Dominio.Tarifas;
using static Modulo.Despachos.Tests.Dominio.Tarifas.TarifasTestData;

namespace Modulo.Despachos.Tests.Dominio.Tarifas;

public class SurchargeConditionTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void DistinctZonesOver_ContaZonasDistintasYExtras()
    {
        var trip = Trip(loc: new[] { Rancagua, SantiagoCentro, new TripLocality(3, 20, "13", "Providencia") });
        var ev = new DistinctZonesOverCondition().Evaluate(J("{\"threshold\":1}"), trip);
        Assert.True(ev.IsMet);
        Assert.Equal(1, ev.ExtraUnits); // 2 zonas distintas - 1
        Assert.False(new DistinctZonesOverCondition().Evaluate(J("{\"threshold\":2}"), trip).IsMet);
    }

    [Fact]
    public void DistinctCommunesOver_ContaComunasDistintas()
    {
        var trip = Trip(loc: new[] { Rancagua, Rancagua, SantiagoCentro });
        var ev = new DistinctCommunesOverCondition().Evaluate(J("{\"threshold\":1}"), trip);
        Assert.True(ev.IsMet);
        Assert.Equal(1, ev.ExtraUnits);
    }

    [Fact]
    public void DepartureTimeFrom_Inclusivo()
    {
        var c = new DepartureTimeFromCondition();
        Assert.True(c.Evaluate(J("{\"time\":\"17:00\"}"), Trip(new TimeOnly(17, 0))).IsMet);
        Assert.False(c.Evaluate(J("{\"time\":\"17:00\"}"), Trip(new TimeOnly(16, 59))).IsMet);
    }

    [Fact]
    public void ShiftIs_YVehicleTypeIs()
    {
        Assert.True(new ShiftIsCondition().Evaluate(J("{\"shiftId\":3}"), Trip(shift: 3)).IsMet);
        Assert.False(new ShiftIsCondition().Evaluate(J("{\"shiftId\":3}"), Trip(shift: null)).IsMet);
        Assert.True(new VehicleTypeIsCondition().Evaluate(J("{\"vehicleTypeId\":4}"), Trip(vehicleType: 4)).IsMet);
        Assert.False(new VehicleTypeIsCondition().Evaluate(J("{\"vehicleTypeId\":4}"), Trip(vehicleType: 5)).IsMet);
    }

    [Fact]
    public void WeekdayIn_UsaDayOfWeek_DomingoCero()
    {
        var domingo = new DateOnly(2026, 11, 15);
        var sabado = new DateOnly(2026, 11, 14);
        var c = new WeekdayInCondition();
        Assert.True(c.Evaluate(J("{\"days\":[6,0]}"), Trip(fecha: domingo)).IsMet);
        Assert.True(c.Evaluate(J("{\"days\":[6,0]}"), Trip(fecha: sabado)).IsMet);
        Assert.False(c.Evaluate(J("{\"days\":[6,0]}"), Trip()).IsMet); // martes
    }

    [Fact]
    public void IsHoliday()
    {
        Assert.True(new IsHolidayCondition().Evaluate(J("{}"), Trip(holiday: true)).IsMet);
        Assert.False(new IsHolidayCondition().Evaluate(J("{}"), Trip(holiday: false)).IsMet);
    }

    [Fact]
    public void DocumentsOver_YPackagesOver_ExtraUnitsEsValorMenosUmbral()
    {
        var d = new DocumentsOverCondition().Evaluate(J("{\"threshold\":3}"), Trip(docs: 8));
        Assert.True(d.IsMet);
        Assert.Equal(5, d.ExtraUnits);
        Assert.False(new DocumentsOverCondition().Evaluate(J("{\"threshold\":8}"), Trip(docs: 8)).IsMet);
        var p = new PackagesOverCondition().Evaluate(J("{\"threshold\":4}"), Trip(packages: 10));
        Assert.True(p.IsMet);
        Assert.Equal(6, p.ExtraUnits);
    }

    [Fact]
    public void ParametroFaltante_Lanza()
    {
        Assert.ThrowsAny<Exception>(() => new DocumentsOverCondition().Evaluate(J("{}"), Trip()));
    }
}
