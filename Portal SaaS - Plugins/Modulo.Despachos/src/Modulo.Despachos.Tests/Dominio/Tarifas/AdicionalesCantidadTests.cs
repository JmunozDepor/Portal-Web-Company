using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;
using static Modulo.Despachos.Tests.Dominio.Tarifas.TarifasTestData;

namespace Modulo.Despachos.Tests.Dominio.Tarifas;

/// <summary>Recargos "manual_quantity": la cantidad la ingresa logística en el manifiesto (tarifa real TSM 2026-10).</summary>
public class AdicionalesCantidadTests
{
    private const long Camion11 = 7, Nocturno = 3;

    private static RateCard[] CardsTsm() => new[]
    {
        new RateCard
        {
            Id = 1, CarrierId = 1, VehicleTypeId = Camion11, Amount = 92612, CalculationMethod = "fixed_per_trip",
            ValidFrom = new DateOnly(2026, 1, 1),
        },
    };

    private static RateSurcharge[] RecargosTsm() => new[]
    {
        Surcharge(20, "Nocturno", "shift_is", "{\"shiftId\":3}", "fixed_amount", 25589, carrier: 1),
        Surcharge(21, "Ayudante adicional", "manual_quantity", null!, "fixed_per_extra_unit", 24455, carrier: 1),
        Surcharge(22, "Punto adicional (Mall extra)", "manual_quantity", null!, "fixed_per_extra_unit", 11975, carrier: 1),
    };

    [Fact]
    public void TarifaTsm_Camion11Nocturno_UnAyudante_DosPuntos()
    {
        var trip = Trip(vehicleType: Camion11, shift: Nocturno, manual: new Dictionary<long, int> { [21] = 1, [22] = 2 });

        var r = Engine().Calculate(trip, CardsTsm(), RecargosTsm());

        Assert.Equal("ok", r.Status);
        Assert.Equal(92612m + 25589m + 24455m + 2 * 11975m, r.Total);
        var punto = r.Lines.Single(l => l.RateSurchargeId == 22);
        Assert.Equal(23950m, punto.Amount);
        Assert.Contains("Cantidad ingresada: 2", punto.Explanation);
        Assert.Contains("2 × $11.975 = $23.950", punto.Explanation);
    }

    [Fact]
    public void SinCantidadIngresada_ElAdicionalNoAplica()
    {
        var r = Engine().Calculate(Trip(vehicleType: Camion11), CardsTsm(), RecargosTsm());

        Assert.Equal(92612m, r.Total);
        Assert.DoesNotContain(r.Lines, l => l.Kind == "surcharge");
    }

    [Fact]
    public void CantidadDeOtroTransportista_NoSeCobra()
    {
        var trip = Trip(carrier: 2, manual: new Dictionary<long, int> { [21] = 3 });

        var r = Engine().Calculate(trip, new[] { Card(1, 61500, carrier: 2) }, RecargosTsm());

        Assert.Equal(61500m, r.Total);
    }

    [Fact]
    public void ConMontoFijo_CobraUnaVezSiHayCantidad()
    {
        var recargo = Surcharge(30, "Peoneta", "manual_quantity", null!, "fixed_amount", 10000);

        var r = Engine().Calculate(Trip(manual: new Dictionary<long, int> { [30] = 4 }), new[] { Card(1, 50000) }, new[] { recargo });

        Assert.Equal(60000m, r.Total);
    }
}
