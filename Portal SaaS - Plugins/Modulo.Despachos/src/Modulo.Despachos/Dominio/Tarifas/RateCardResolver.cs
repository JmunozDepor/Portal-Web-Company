using Modulo.Despachos.Models;

namespace Modulo.Despachos.Dominio.Tarifas;

public record RateResolution(RateCard? Card, bool IsAmbiguous);

/// <summary>Elige la tarifa más específica para una localidad del viaje.</summary>
public static class RateCardResolver
{
    public static RateResolution Resolve(IEnumerable<RateCard> cards, TripCostInput trip, TripLocality locality)
    {
        var candidates = cards
            .Where(c => Matches(c, trip, locality))
            .Select(c => (Card: c, Score: Specificity(c)))
            .ToList();
        if (candidates.Count == 0) return new RateResolution(null, false);

        var top = candidates.Max(x => x.Score);
        var best = candidates.Where(x => x.Score == top).Select(x => x.Card).ToList();
        var topPriority = best.Max(c => c.Priority);
        var winners = best.Where(c => c.Priority == topPriority).ToList();
        return winners.Count == 1
            ? new RateResolution(winners[0], false)
            : new RateResolution(null, true);
    }

    /// <summary>Condiciones no nulas; la geográfica aporta 3 (comuna), 2 (zona) o 1 (región), solo la más específica.</summary>
    public static int Specificity(RateCard c)
    {
        var score = 0;
        if (c.CarrierId != null) score++;
        if (c.ShiftId != null) score++;
        if (c.VehicleTypeId != null) score++;
        if (c.M3From != null || c.M3To != null) score++;
        if (c.CommuneId != null) score += 3;
        else if (c.ZoneId != null) score += 2;
        else if (c.RegionCode != null) score += 1;
        return score;
    }

    public static bool Matches(RateCard c, TripCostInput trip, TripLocality loc)
    {
        if (!c.IsActive) return false;
        if (trip.DispatchDate < c.ValidFrom) return false;
        if (c.ValidTo != null && trip.DispatchDate > c.ValidTo) return false;
        if (c.CarrierId != null && c.CarrierId != trip.CarrierId) return false;
        if (c.ShiftId != null && c.ShiftId != trip.ShiftId) return false;
        if (c.VehicleTypeId != null && c.VehicleTypeId != trip.VehicleTypeId) return false;
        if (c.CommuneId != null && c.CommuneId != loc.CommuneId) return false;
        if (c.ZoneId != null && c.ZoneId != loc.ZoneId) return false;
        if (c.RegionCode != null && c.RegionCode != loc.RegionCode) return false;
        if (c.M3From != null || c.M3To != null)
        {
            if (trip.CapacityM3 == null) return false;
            if (c.M3From != null && trip.CapacityM3 < c.M3From) return false;
            if (c.M3To != null && trip.CapacityM3 > c.M3To) return false;
        }
        return true;
    }
}
