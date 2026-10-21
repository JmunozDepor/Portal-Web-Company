using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Servicios;

public sealed class ManifestCostService : IManifestCostService
{
    private readonly DespachosDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ITariffService _tariff;
    private readonly CostEngine _engine;
    private readonly TimeProvider _time;

    public ManifestCostService(
        DespachosDbContext db, ICurrentCompanyAccessor company, ITariffService tariff, CostEngine engine, TimeProvider time)
    {
        _db = db;
        _company = company;
        _tariff = tariff;
        _engine = engine;
        _time = time;
    }

    private Guid CompanyId => _company.CompanyId;

    public async Task<CostResult> RecalculateAsync(Manifest m)
    {
        if (m.CompanyId != CompanyId) throw new InvalidOperationException("El manifiesto no existe.");

        var docs = await _db.ManifestDocuments
            .Where(d => d.CompanyId == CompanyId && d.ManifestId == m.Id).ToListAsync();

        CostResult result;
        // Comuna sin resolver (o sin documentos): no hay con qué buscar tarifa -> "sin tarifa" (spec §4.2c paso 6).
        if (docs.Count == 0 || docs.Any(d => d.DestinationCommuneId == null))
        {
            result = new CostResult(CostStatuses.NoRate, null, 0m, Array.Empty<CostLine>());
        }
        else
        {
            var communeIds = docs.Select(d => d.DestinationCommuneId!.Value).Distinct().ToList();
            var km = m.OdometerStart != null && m.OdometerEnd != null && m.OdometerEnd >= m.OdometerStart
                ? (decimal?)(m.OdometerEnd - m.OdometerStart) : null;
            var input = new SimulationInput(
                m.CarrierId, m.ShiftId, m.VehicleId, m.DispatchDate, LocalDepartureTime(m), communeIds,
                docs.Count, (int)Math.Round(docs.Sum(d => d.Units), MidpointRounding.AwayFromZero),
                docs.Sum(d => d.Packages), km);
            var trip = await _tariff.BuildTripAsync(input);
            var (cards, surcharges) = await _tariff.LoadActiveAsync();
            result = _engine.Calculate(trip, cards, surcharges);
        }

        // Reemplaza el detalle anterior.
        _db.ManifestCostLines.RemoveRange(
            await _db.ManifestCostLines.Where(l => l.CompanyId == CompanyId && l.ManifestId == m.Id).ToListAsync());

        var perCommune = docs.Where(d => d.DestinationCommuneId != null)
            .GroupBy(d => d.DestinationCommuneId!.Value)
            .ToDictionary(g => g.Key, g => (Documents: g.Count(), Units: g.Sum(d => d.Units), Packages: g.Sum(d => d.Packages)));
        foreach (var line in result.Lines)
        {
            var counts = line.Kind == "base" && line.CommuneId is { } cid && perCommune.TryGetValue(cid, out var c)
                ? c : (Documents: 0, Units: 0m, Packages: 0);
            _db.ManifestCostLines.Add(new ManifestCostLine
            {
                CompanyId = CompanyId, ManifestId = m.Id, Kind = line.Kind, CommuneId = line.CommuneId,
                RateCardId = line.RateCardId, RateSurchargeId = line.RateSurchargeId, Amount = line.Amount,
                Explanation = line.Explanation.Length > 500 ? line.Explanation[..500] : line.Explanation,
                Documents = counts.Documents, Units = counts.Units, Packages = counts.Packages,
            });
        }

        var ok = result.Status == CostStatuses.Ok;
        m.CostStatus = result.Status;
        m.CostCalculated = ok ? result.Total : null;
        m.RateCardId = ok
            ? result.Lines.FirstOrDefault(l => l.Kind == "base" && l.RateCardId != null && l.Amount == result.BaseAmount)?.RateCardId
            : null;
        m.CostFinal = m.CostOverride ?? m.CostCalculated;
        m.UpdatedAt = _time.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync();
        return result;
    }

    /// <summary>Hora de salida en hora Chile: la real si ya despachó; si no, la planificada (o 00:00).</summary>
    private static TimeOnly LocalDepartureTime(Manifest m) =>
        m.DepartedAt is { } utc
            ? TimeOnly.FromDateTime(ChileClock.ToLocal(utc))
            : m.PlannedDepartureTime ?? TimeOnly.MinValue;
}
