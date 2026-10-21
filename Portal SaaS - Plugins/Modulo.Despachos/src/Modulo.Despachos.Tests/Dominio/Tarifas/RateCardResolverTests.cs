using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;
using static Modulo.Despachos.Tests.Dominio.Tarifas.TarifasTestData;

namespace Modulo.Despachos.Tests.Dominio.Tarifas;

public class RateCardResolverTests
{
    [Fact]
    public void Resolver_ComunaGanaAZonaYRegion()
    {
        var cards = new[]
        {
            Card(1, 200000, region: "06"),
            Card(2, 180000, zone: 10),
            Card(3, 150000, commune: 1)
        };
        var r = RateCardResolver.Resolve(cards, Trip(), Rancagua);
        Assert.False(r.IsAmbiguous);
        Assert.Equal(3, r.Card!.Id);
    }

    [Fact]
    public void Resolver_ZonaGanaARegion()
    {
        var cards = new[] { Card(1, 200000, region: "06"), Card(2, 180000, zone: 10) };
        Assert.Equal(2, RateCardResolver.Resolve(cards, Trip(), Rancagua).Card!.Id);
    }

    [Fact]
    public void Resolver_MasCondicionesGanaAGeograficaMasEspecifica()
    {
        // comuna (3) vs región + transportista + turno (1+1+1 = 3) + ... el de 4 condiciones gana
        var a = Card(1, 100, commune: 1);
        var b = Card(2, 200, region: "06", carrier: 1);
        b.ShiftId = 5;
        b.VehicleTypeId = 7;
        var r = RateCardResolver.Resolve(new[] { a, b }, Trip(shift: 5, vehicleType: 7), Rancagua);
        Assert.Equal(2, r.Card!.Id);
    }

    [Fact]
    public void Resolver_EmpatePrioridad_GanaMayorPriority()
    {
        var cards = new[] { Card(1, 100, commune: 1, priority: 1), Card(2, 200, commune: 1, priority: 5) };
        var r = RateCardResolver.Resolve(cards, Trip(), Rancagua);
        Assert.False(r.IsAmbiguous);
        Assert.Equal(2, r.Card!.Id);
    }

    [Fact]
    public void Resolver_EmpateTotal_IsAmbiguous()
    {
        var cards = new[] { Card(1, 100, commune: 1, priority: 1), Card(2, 200, commune: 1, priority: 1) };
        var r = RateCardResolver.Resolve(cards, Trip(), Rancagua);
        Assert.True(r.IsAmbiguous);
        Assert.Null(r.Card);
    }

    [Fact]
    public void Resolver_IgnoraFueraDeVigenciaEInactivas()
    {
        var vencida = Card(1, 100, commune: 1);
        vencida.ValidTo = new DateOnly(2026, 11, 9);
        var futura = Card(2, 100, commune: 1);
        futura.ValidFrom = new DateOnly(2026, 11, 11);
        var inactiva = Card(3, 100, commune: 1);
        inactiva.IsActive = false;
        var vigente = Card(4, 999, region: "06");
        var r = RateCardResolver.Resolve(new[] { vencida, futura, inactiva, vigente }, Trip(), Rancagua);
        Assert.Equal(4, r.Card!.Id);
    }

    [Fact]
    public void Resolver_VigenciaEsInclusivaEnLosExtremos()
    {
        var c = Card(1, 100, commune: 1);
        c.ValidFrom = Fecha;
        c.ValidTo = Fecha;
        Assert.Equal(1, RateCardResolver.Resolve(new[] { c }, Trip(), Rancagua).Card!.Id);
    }

    [Fact]
    public void Resolver_TramoM3_UsaCapacidad()
    {
        var c = Card(1, 100, commune: 1);
        c.M3From = 20;
        c.M3To = 40;
        Assert.Equal(1, RateCardResolver.Resolve(new[] { c }, Trip(m3: 30), Rancagua).Card!.Id);
        Assert.Equal(1, RateCardResolver.Resolve(new[] { c }, Trip(m3: 20), Rancagua).Card!.Id);
        Assert.Equal(1, RateCardResolver.Resolve(new[] { c }, Trip(m3: 40), Rancagua).Card!.Id);
        Assert.Null(RateCardResolver.Resolve(new[] { c }, Trip(m3: 45), Rancagua).Card);
        Assert.Null(RateCardResolver.Resolve(new[] { c }, Trip(m3: 19.99m), Rancagua).Card);
    }

    [Fact]
    public void Resolver_TramoM3_SinCapacidadDelViaje_NoCalza()
    {
        var c = Card(1, 100, commune: 1);
        c.M3From = 20;
        Assert.Null(RateCardResolver.Resolve(new[] { c }, Trip(m3: null), Rancagua).Card);
    }

    [Fact]
    public void Resolver_TramoM3_LimitesNulosSonAbiertos()
    {
        var soloDesde = Card(1, 100, commune: 1);
        soloDesde.M3From = 20;
        var soloHasta = Card(2, 100, commune: 1);
        soloHasta.M3To = 10;
        Assert.Equal(1, RateCardResolver.Resolve(new[] { soloDesde }, Trip(m3: 500), Rancagua).Card!.Id);
        Assert.Equal(2, RateCardResolver.Resolve(new[] { soloHasta }, Trip(m3: 5), Rancagua).Card!.Id);
    }

    [Fact]
    public void Resolver_TransportistaDistinto_NoCalza()
    {
        var c = Card(1, 100, commune: 1, carrier: 99);
        Assert.Null(RateCardResolver.Resolve(new[] { c }, Trip(carrier: 1), Rancagua).Card);
    }

    [Fact]
    public void Resolver_ComunaDistinta_NoCalza()
    {
        var c = Card(1, 100, commune: 2);
        Assert.Null(RateCardResolver.Resolve(new[] { c }, Trip(), Rancagua).Card);
    }
}
