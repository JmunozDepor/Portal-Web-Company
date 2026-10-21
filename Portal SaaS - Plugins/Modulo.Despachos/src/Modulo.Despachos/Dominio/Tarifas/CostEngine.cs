using System.Text.Json;
using Modulo.Despachos.Models;

namespace Modulo.Despachos.Dominio.Tarifas;

/// <summary>
/// Costo del viaje = valor base (máximo entre las localidades) + recargos (suman; los % van sobre
/// el base, sin cascada). Todo monto se redondea a peso entero (AwayFromZero). Puro, sin EF.
/// </summary>
public class CostEngine
{
    private readonly Dictionary<string, IRateCalculator> _calculators;
    private readonly Dictionary<string, ISurchargeCondition> _conditions;

    public CostEngine(IEnumerable<IRateCalculator> calculators, IEnumerable<ISurchargeCondition> conditions)
    {
        _calculators = calculators.ToDictionary(c => c.Method);
        _conditions = conditions.ToDictionary(c => c.Type);
    }

    public CostResult Calculate(TripCostInput trip, IReadOnlyList<RateCard> cards, IReadOnlyList<RateSurcharge> surcharges)
    {
        var resolved = new List<(TripLocality Loc, RateCard Card, decimal Amount)>();
        var lines = new List<CostLine>();
        var anyAmbiguous = false;
        var anyMissing = trip.Localities.Count == 0;

        foreach (var loc in trip.Localities)
        {
            var r = RateCardResolver.Resolve(cards, trip, loc);
            if (r.IsAmbiguous)
            {
                anyAmbiguous = true;
                lines.Add(new CostLine("base", loc.CommuneId, null, null, 0m, $"Tarifa ambigua para {loc.CommuneName}"));
            }
            else if (r.Card == null)
            {
                anyMissing = true;
                lines.Add(new CostLine("base", loc.CommuneId, null, null, 0m, $"Sin tarifa para {loc.CommuneName}"));
            }
            else
            {
                if (!_calculators.TryGetValue(r.Card.CalculationMethod, out var calc))
                    throw new InvalidOperationException(
                        $"Modalidad de cálculo desconocida '{r.Card.CalculationMethod}' en la tarifa {r.Card.Id}.");
                resolved.Add((loc, r.Card, calc.Calculate(r.Card, trip)));
            }
        }

        // Sin tarifa o ambigua: no se calculan recargos ni total.
        if (anyAmbiguous || anyMissing)
            return new CostResult(anyAmbiguous ? "ambiguous" : "no_rate", null, 0m, lines);

        var baseAmount = resolved.Max(x => x.Amount);
        var winnerIdx = resolved.FindIndex(x => x.Amount == baseAmount);
        lines.Clear();
        for (var i = 0; i < resolved.Count; i++)
        {
            var (loc, card, amount) = resolved[i];
            lines.Add(i == winnerIdx
                ? new CostLine("base", loc.CommuneId, card.Id, null, amount, $"Valor base {loc.CommuneName} ({card.CalculationMethod})")
                : new CostLine("base", loc.CommuneId, card.Id, null, 0m, $"{loc.CommuneName}: incluida en destino de mayor valor"));
        }

        var total = baseAmount;
        var applicable = surcharges
            .Where(s => s.IsActive
                        && (s.CarrierId == null || s.CarrierId == trip.CarrierId)
                        && trip.DispatchDate >= s.ValidFrom
                        && (s.ValidTo == null || trip.DispatchDate <= s.ValidTo))
            .OrderBy(s => s.Priority).ThenBy(s => s.Id);
        foreach (var s in applicable)
        {
            var ev = Evaluate(s, trip);
            if (!ev.IsMet) continue;
            var amount = Money.Round(s.EffectType switch
            {
                "fixed_amount" => s.Value,
                "percent_of_base" => baseAmount * s.Value / 100m,
                "fixed_per_extra_unit" => s.Value * ev.ExtraUnits,
                _ => throw new InvalidOperationException(
                    $"Efecto de recargo desconocido '{s.EffectType}' en el recargo '{s.Name}'.")
            });
            total += amount;
            lines.Add(new CostLine("surcharge", null, null, s.Id, amount, $"{s.Name}: {ev.Explanation}"));
        }

        return new CostResult("ok", total, baseAmount, lines);
    }

    private SurchargeEvaluation Evaluate(RateSurcharge s, TripCostInput trip)
    {
        if (!_conditions.TryGetValue(s.ConditionType, out var cond))
            throw new InvalidOperationException(
                $"Tipo de condición desconocido '{s.ConditionType}' en el recargo '{s.Name}'.");
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(s.ConditionParametersJson) ? "{}" : s.ConditionParametersJson);
            return cond.Evaluate(doc.RootElement, trip);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException
                                       or FormatException or ArgumentException or OverflowException)
        {
            throw new InvalidOperationException(
                $"Parámetros inválidos en la condición del recargo '{s.Name}': {ex.Message}", ex);
        }
    }
}
