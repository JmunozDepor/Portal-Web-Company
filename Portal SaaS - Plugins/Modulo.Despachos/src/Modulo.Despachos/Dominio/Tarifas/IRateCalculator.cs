using Modulo.Despachos.Models;

namespace Modulo.Despachos.Dominio.Tarifas;

/// <summary>Estrategia de cálculo del valor base. Una modalidad nueva = una clase.</summary>
public interface IRateCalculator
{
    string Method { get; }
    decimal Calculate(RateCard card, TripCostInput trip);
}
