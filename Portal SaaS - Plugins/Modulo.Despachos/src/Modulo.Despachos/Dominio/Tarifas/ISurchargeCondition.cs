using System.Text.Json;

namespace Modulo.Despachos.Dominio.Tarifas;

public record SurchargeEvaluation(bool IsMet, int ExtraUnits, string Explanation);

/// <summary>Condición de recargo. Un tipo nuevo = una clase. Parámetros inválidos lanzan excepción.</summary>
public interface ISurchargeCondition
{
    string Type { get; }
    SurchargeEvaluation Evaluate(JsonElement parameters, TripCostInput trip);
}
