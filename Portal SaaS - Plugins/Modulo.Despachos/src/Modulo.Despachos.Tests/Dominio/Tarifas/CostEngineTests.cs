using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;
using static Modulo.Despachos.Tests.Dominio.Tarifas.TarifasTestData;

namespace Modulo.Despachos.Tests.Dominio.Tarifas;

public class CostEngineTests
{
    private static readonly TripLocality[] DosZonas = { Rancagua, SantiagoCentro };

    private static RateCard[] CardsJefe() => new[]
    {
        Card(1, 150000, commune: 1),
        Card(2, 90000, commune: 2)
    };

    private static RateSurcharge[] RecargosJefe() => new[]
    {
        Surcharge(10, "Después de las 17:00", "departure_time_from", "{\"time\":\"17:00\"}", "percent_of_base", 20),
        Surcharge(11, "Dos zonas", "distinct_zones_over", "{\"threshold\":1}", "fixed_amount", 25000)
    };

    [Fact]
    public void Engine_EjemploJefeDespachos()
    {
        var r = Engine().Calculate(Trip(new TimeOnly(17, 42), DosZonas), CardsJefe(), RecargosJefe());

        Assert.Equal("ok", r.Status);
        Assert.Equal(205000m, r.Total);
        Assert.Equal(150000m, r.BaseAmount);
        Assert.Equal(4, r.Lines.Count);
        var bases = r.Lines.Where(l => l.Kind == "base").ToList();
        Assert.Equal(new[] { 150000m, 0m }, bases.Select(l => l.Amount).ToArray());
        Assert.Contains("incluida en destino de mayor valor", bases[1].Explanation);
        var sur = r.Lines.Where(l => l.Kind == "surcharge").ToList();
        Assert.Equal(new[] { 30000m, 25000m }, sur.Select(l => l.Amount).ToArray());
        Assert.Contains("17:42", sur[0].Explanation);
        Assert.Contains("17:00", sur[0].Explanation);
        Assert.Equal(10, sur[0].RateSurchargeId);
    }

    [Fact]
    public void Engine_BaseEsElMaximo_AunqueLaMayorNoSeaLaPrimera()
    {
        var cards = new[] { Card(1, 90000, commune: 1), Card(2, 150000, commune: 2) };
        var r = Engine().Calculate(Trip(loc: DosZonas), cards, Array.Empty<RateSurcharge>());
        Assert.Equal(150000m, r.BaseAmount);
        Assert.Equal(new[] { 0m, 150000m }, r.Lines.Select(l => l.Amount).ToArray());
    }

    [Fact]
    public void Engine_SalidaAntesDeLas17_SinRecargoHorario()
    {
        var r = Engine().Calculate(Trip(new TimeOnly(16, 59), DosZonas), CardsJefe(), RecargosJefe());
        Assert.Equal(175000m, r.Total);
        Assert.Equal(3, r.Lines.Count);
    }

    [Fact]
    public void Engine_SalidaExactamenteALas17_AplicaRecargo()
    {
        var r = Engine().Calculate(Trip(new TimeOnly(17, 0), DosZonas), CardsJefe(), RecargosJefe());
        Assert.Equal(205000m, r.Total);
    }

    [Fact]
    public void Engine_PorcentajesNoEnCascada()
    {
        var cards = new[] { Card(1, 100000, commune: 1) };
        var rec = new[]
        {
            Surcharge(1, "A", "is_holiday", "{}", "percent_of_base", 20),
            Surcharge(2, "B", "is_holiday", "{}", "percent_of_base", 10)
        };
        var r = Engine().Calculate(Trip(holiday: true), cards, rec);
        Assert.Equal(130000m, r.Total);
    }

    [Fact]
    public void Engine_FixedPerExtraUnit()
    {
        var l3 = new TripLocality(3, 30, "13", "Maipú");
        var cards = new[] { Card(1, 100000, commune: 1), Card(2, 50000, commune: 2), Card(3, 50000, commune: 3) };
        var rec = new[] { Surcharge(1, "Zona extra", "distinct_zones_over", "{\"threshold\":1}", "fixed_per_extra_unit", 10000) };
        var r = Engine().Calculate(Trip(loc: new[] { Rancagua, SantiagoCentro, l3 }), cards, rec);
        Assert.Equal(120000m, r.Total);
        Assert.Equal(20000m, r.Lines.Single(l => l.Kind == "surcharge").Amount);
    }

    [Fact]
    public void Engine_ZonasDistintas_CuentaSoloZonasNoNulasDistintas()
    {
        // dos comunas de la misma zona + una sin zona = 1 zona distinta -> no supera umbral 1
        var misma = new TripLocality(5, 10, "06", "Machalí");
        var sinZona = new TripLocality(6, null, "06", "Rural");
        var cards = new[]
        {
            Card(1, 100, commune: 1), Card(2, 100, commune: 5), Card(3, 100, commune: 6)
        };
        var rec = new[] { Surcharge(1, "Zonas", "distinct_zones_over", "{\"threshold\":1}", "fixed_amount", 999) };
        var r = Engine().Calculate(Trip(loc: new[] { Rancagua, misma, sinZona }), cards, rec);
        Assert.Equal(100m, r.Total);
    }

    [Fact]
    public void Engine_LocalidadSinTarifa_StatusNoRate_TotalNull()
    {
        var cards = new[] { Card(1, 150000, commune: 1) }; // falta Santiago
        var r = Engine().Calculate(Trip(loc: DosZonas), cards, RecargosJefe());
        Assert.Equal("no_rate", r.Status);
        Assert.Null(r.Total);
        Assert.DoesNotContain(r.Lines, l => l.Kind == "surcharge");
    }

    [Fact]
    public void Engine_ViajeSinLocalidades_NoRate()
    {
        var r = Engine().Calculate(Trip(loc: Array.Empty<TripLocality>()), CardsJefe(), Array.Empty<RateSurcharge>());
        Assert.Equal("no_rate", r.Status);
        Assert.Null(r.Total);
    }

    [Fact]
    public void Engine_Ambigua_StatusAmbiguous()
    {
        var cards = new[] { Card(1, 100, commune: 1), Card(2, 200, commune: 1) };
        var r = Engine().Calculate(Trip(), cards, Array.Empty<RateSurcharge>());
        Assert.Equal("ambiguous", r.Status);
        Assert.Null(r.Total);
    }

    [Fact]
    public void Engine_MinAmount()
    {
        var cards = new[] { Card(1, 500, "per_package", commune: 1, min: 8000) };
        var r = Engine().Calculate(Trip(packages: 10), cards, Array.Empty<RateSurcharge>());
        Assert.Equal(8000m, r.BaseAmount);
        Assert.Equal(8000m, r.Total);
    }

    [Fact]
    public void Engine_MinAmount_NoReduceCuandoElCalculoLoSupera()
    {
        var cards = new[] { Card(1, 500, "per_package", commune: 1, min: 8000) };
        Assert.Equal(10000m, Engine().Calculate(Trip(packages: 20), cards, Array.Empty<RateSurcharge>()).BaseAmount);
    }

    [Fact]
    public void Engine_NoCharge_RetiroCero()
    {
        var cards = new[] { Card(1, 5000, "no_charge", commune: 1, min: 3000) };
        var r = Engine().Calculate(Trip(), cards, Array.Empty<RateSurcharge>());
        Assert.Equal("ok", r.Status);
        Assert.Equal(0m, r.Total);
    }

    [Fact]
    public void Engine_PerKm_KmNulo_SeTrataComoCeroYAplicaMinimo()
    {
        var cards = new[] { Card(1, 800, "per_km", commune: 1, min: 12000) };
        Assert.Equal(12000m, Engine().Calculate(Trip(km: null), cards, Array.Empty<RateSurcharge>()).BaseAmount);
        Assert.Equal(16000m, Engine().Calculate(Trip(km: 20), cards, Array.Empty<RateSurcharge>()).BaseAmount);
    }

    [Fact]
    public void Engine_PerM3_UsaCapacidadDelCamion()
    {
        var cards = new[] { Card(1, 1000, "per_m3", commune: 1) };
        Assert.Equal(30000m, Engine().Calculate(Trip(m3: 30), cards, Array.Empty<RateSurcharge>()).BaseAmount);
    }

    [Fact]
    public void Engine_PerDocument_UsaTotalesDelViaje()
    {
        var cards = new[] { Card(1, 2000, "per_document", commune: 1), Card(2, 2000, "per_document", commune: 2) };
        var r = Engine().Calculate(Trip(loc: DosZonas, docs: 7), cards, Array.Empty<RateSurcharge>());
        Assert.Equal(14000m, r.BaseAmount); // no se duplica por localidad
    }

    [Fact]
    public void Engine_RedondeaAPesoAwayFromZero()
    {
        var cards = new[] { Card(1, 100.5m, "per_package", commune: 1) };
        Assert.Equal(1005m, Engine().Calculate(Trip(packages: 10), cards, Array.Empty<RateSurcharge>()).BaseAmount);
        var cards2 = new[] { Card(1, 0.5m, "per_package", commune: 1) };
        Assert.Equal(3m, Engine().Calculate(Trip(packages: 5), cards2, Array.Empty<RateSurcharge>()).BaseAmount); // 2.5 -> 3
        var cards3 = new[] { Card(1, 1001, commune: 1) };
        var rec = new[] { Surcharge(1, "p", "is_holiday", "{}", "percent_of_base", 12.5m) }; // 125.125 -> 125
        Assert.Equal(1126m, Engine().Calculate(Trip(holiday: true), cards3, rec).Total);
        var rec2 = new[] { Surcharge(1, "p", "is_holiday", "{}", "percent_of_base", 50m) }; // 500.5 -> 501
        Assert.Equal(1502m, Engine().Calculate(Trip(holiday: true), cards3, rec2).Total);
    }

    [Fact]
    public void Engine_RecargoDeOtroTransportista_NoAplica()
    {
        var cards = new[] { Card(1, 100000, commune: 1) };
        var rec = new[]
        {
            Surcharge(1, "Otro", "is_holiday", "{}", "fixed_amount", 5000, carrier: 99),
            Surcharge(2, "Mío", "is_holiday", "{}", "fixed_amount", 7000, carrier: 1)
        };
        Assert.Equal(107000m, Engine().Calculate(Trip(holiday: true, carrier: 1), cards, rec).Total);
    }

    [Fact]
    public void Engine_RecargoInactivoOFueraDeVigencia_NoAplica()
    {
        var cards = new[] { Card(1, 100000, commune: 1) };
        var inactivo = Surcharge(1, "i", "is_holiday", "{}", "fixed_amount", 1);
        inactivo.IsActive = false;
        var vencido = Surcharge(2, "v", "is_holiday", "{}", "fixed_amount", 1);
        vencido.ValidTo = Fecha.AddDays(-1);
        Assert.Equal(100000m, Engine().Calculate(Trip(holiday: true), cards, new[] { inactivo, vencido }).Total);
    }

    [Fact]
    public void Engine_RecargoSinCumplirCondicion_NoAgregaLinea()
    {
        var cards = new[] { Card(1, 100000, commune: 1) };
        var rec = new[] { Surcharge(1, "Feriado", "is_holiday", "{}", "fixed_amount", 5000) };
        var r = Engine().Calculate(Trip(holiday: false), cards, rec);
        Assert.Single(r.Lines);
        Assert.Equal(100000m, r.Total);
    }

    [Theory]
    [InlineData("{\"time\":\"abc\"}")]
    [InlineData("{}")]
    [InlineData("no es json")]
    [InlineData("")]
    public void Engine_ParametrosInvalidos_LanzanConNombreDelRecargo(string json)
    {
        var cards = new[] { Card(1, 100000, commune: 1) };
        var rec = new[] { Surcharge(1, "Recargo nocturno", "departure_time_from", json, "fixed_amount", 1) };
        var ex = Assert.Throws<InvalidOperationException>(() => Engine().Calculate(Trip(), cards, rec));
        Assert.Contains("Recargo nocturno", ex.Message);
    }

    [Fact]
    public void Engine_TipoDeCondicionDesconocido_LanzaConNombreDelRecargo()
    {
        var cards = new[] { Card(1, 100000, commune: 1) };
        var rec = new[] { Surcharge(1, "Raro", "no_existe", "{}", "fixed_amount", 1) };
        var ex = Assert.Throws<InvalidOperationException>(() => Engine().Calculate(Trip(), cards, rec));
        Assert.Contains("Raro", ex.Message);
    }

    [Fact]
    public void Engine_EfectoDesconocido_LanzaConNombreDelRecargo()
    {
        var cards = new[] { Card(1, 100000, commune: 1) };
        var rec = new[] { Surcharge(1, "Efecto raro", "is_holiday", "{}", "magia", 1) };
        var ex = Assert.Throws<InvalidOperationException>(() => Engine().Calculate(Trip(holiday: true), cards, rec));
        Assert.Contains("Efecto raro", ex.Message);
    }
}
