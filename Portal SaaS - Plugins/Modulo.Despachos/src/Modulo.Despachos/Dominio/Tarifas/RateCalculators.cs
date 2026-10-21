using Modulo.Despachos.Models;

namespace Modulo.Despachos.Dominio.Tarifas;

public static class Money
{
    /// <summary>Peso entero, mitades hacia arriba (AwayFromZero).</summary>
    public static decimal Round(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);
}

/// <summary>Base de las estrategias: calcula en bruto, redondea a peso y aplica el mínimo.</summary>
public abstract class RateCalculatorBase : IRateCalculator
{
    public abstract string Method { get; }
    protected abstract decimal Raw(RateCard card, TripCostInput trip);

    public decimal Calculate(RateCard card, TripCostInput trip)
    {
        var calc = Money.Round(Raw(card, trip));
        return Math.Max(calc, Money.Round(card.MinAmount ?? 0m));
    }
}

public sealed class FixedPerTripCalculator : RateCalculatorBase
{
    public override string Method => "fixed_per_trip";
    protected override decimal Raw(RateCard c, TripCostInput t) => c.Amount;
}

/// <summary>Cobra sobre la capacidad del camión (no el volumen cargado). Sin capacidad = 0.</summary>
public sealed class PerM3Calculator : RateCalculatorBase
{
    public override string Method => "per_m3";
    protected override decimal Raw(RateCard c, TripCostInput t) => c.Amount * (t.CapacityM3 ?? 0m);
}

/// <summary>Km nulo se trata como 0 y luego rige el monto mínimo.</summary>
public sealed class PerKmCalculator : RateCalculatorBase
{
    public override string Method => "per_km";
    protected override decimal Raw(RateCard c, TripCostInput t) => c.Amount * (t.Km ?? 0m);
}

public sealed class PerPackageCalculator : RateCalculatorBase
{
    public override string Method => "per_package";
    protected override decimal Raw(RateCard c, TripCostInput t) => c.Amount * t.Packages;
}

public sealed class PerDocumentCalculator : RateCalculatorBase
{
    public override string Method => "per_document";
    protected override decimal Raw(RateCard c, TripCostInput t) => c.Amount * t.Documents;
}

/// <summary>RETIRO: sin cobro. Ignora también el mínimo.</summary>
public sealed class NoChargeCalculator : IRateCalculator
{
    public string Method => "no_charge";
    public decimal Calculate(RateCard card, TripCostInput trip) => 0m;
}
