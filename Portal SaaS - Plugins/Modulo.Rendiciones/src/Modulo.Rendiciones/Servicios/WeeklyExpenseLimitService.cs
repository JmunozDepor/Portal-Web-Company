using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class WeeklyExpenseLimitService : IWeeklyExpenseLimitService
{
    private readonly RendicionesDbContext _db;
    private readonly IPolicyProfileService _profiles;

    public WeeklyExpenseLimitService(RendicionesDbContext db, IPolicyProfileService profiles)
    {
        _db = db;
        _profiles = profiles;
    }

    public async Task<IReadOnlyList<WeeklyLimitWarning>> GetWarningsAsync(Guid companyId, Guid userId,
        DateOnly anyDateInWeek, CancellationToken ct = default)
    {
        var weekStart = StartOfWeek(anyDateInWeek);
        var weekEnd = weekStart.AddDays(6);

        var userProfileId = await _profiles.GetAssignedProfileIdAsync(companyId, userId, ct);

        // Políticas Weekly activas candidatas: sin perfil (aplican a cualquiera) o con el
        // perfil exacto del usuario. CityId no se filtra -- Task 5 fuerza CityId=null en
        // toda política Weekly, así que nunca es parte de la resolución acá.
        var candidates = await _db.ExpensePolicies
            .Include(p => p.ExpenseType)
            .Where(p => p.CompanyId == companyId && p.Period == ExpensePolicyPeriod.Weekly && p.IsActive)
            .Where(p => p.PolicyProfileId == null || p.PolicyProfileId == userProfileId)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return Array.Empty<WeeklyLimitWarning>();

        // Si hay una política "comodín" (PolicyProfileId null) y otra específica del perfil
        // para el mismo ExpenseType, la específica gana (mismo criterio de especificidad que
        // ExpensePolicyService.FindApplicablePolicyAsync, acá reducido a un solo eje).
        var applicablePolicies = candidates
            .GroupBy(p => p.ExpenseTypeId)
            .Select(g => g.OrderByDescending(p => p.PolicyProfileId is not null).First());

        var weekStartUtc = new DateTimeOffset(weekStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var weekEndExclusiveUtc = new DateTimeOffset(weekEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var warnings = new List<WeeklyLimitWarning>();
        foreach (var policy in applicablePolicies)
        {
            if (policy.MaxAmount is not { } maxAmount)
                continue;

            var total = await _db.ExpenseReportLines
                .Where(l => l.CompanyId == companyId && l.UserId == userId && l.ExpenseTypeId == policy.ExpenseTypeId)
                .Where(l => l.Date >= weekStartUtc && l.Date < weekEndExclusiveUtc)
                .Where(l => l.ExpenseReportId == null || l.ExpenseReport!.Status != "Rejected")
                .SumAsync(l => (decimal?)l.ReimbursementAmount, ct) ?? 0m;

            if (total > maxAmount)
            {
                warnings.Add(new WeeklyLimitWarning(
                    policy.ExpenseType?.Name ?? string.Empty,
                    weekStart,
                    weekEnd,
                    total,
                    maxAmount,
                    total - maxAmount));
            }
        }

        return warnings;
    }

    /// <summary>Lunes de la semana calendario que contiene <paramref name="date"/>.</summary>
    private static DateOnly StartOfWeek(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff);
    }
}
