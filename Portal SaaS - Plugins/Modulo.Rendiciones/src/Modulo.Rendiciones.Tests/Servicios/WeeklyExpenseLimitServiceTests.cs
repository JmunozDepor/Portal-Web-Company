using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

/// <summary>
/// Cubre `IWeeklyExpenseLimitService.GetWarningsAsync` (Task 8): advertencia agregada
/// semanal por ExpenseType, distinta del tope por documento que resuelve
/// ExpensePolicyService (Task 6/7). Puramente informativa y de solo lectura -- nunca
/// modifica ReimbursementAmount, solo suma lo que ya dejó calculado Task 7.
/// </summary>
public class WeeklyExpenseLimitServiceTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    private static async Task<long> CreateExpenseTypeAsync(RendicionesDbContext db, Guid companyId, string name = "Almuerzo")
    {
        var type = new ExpenseType { CompanyId = companyId, Name = name };
        db.ExpenseTypes.Add(type);
        await db.SaveChangesAsync();
        return type.Id;
    }

    private static async Task<long> AddWeeklyPolicyAsync(RendicionesDbContext db, Guid companyId, long expenseTypeId,
        long? policyProfileId, decimal maxAmount, bool isActive = true)
    {
        var policy = new ExpensePolicy
        {
            CompanyId = companyId,
            ExpenseTypeId = expenseTypeId,
            CityId = null,
            PolicyProfileId = policyProfileId,
            Period = ExpensePolicyPeriod.Weekly,
            MaxAmount = maxAmount,
            IsActive = isActive,
        };
        db.ExpensePolicies.Add(policy);
        await db.SaveChangesAsync();
        return policy.Id;
    }

    private static async Task<long> AddLineAsync(RendicionesDbContext db, Guid companyId, Guid userId,
        long expenseTypeId, DateTimeOffset date, decimal reimbursementAmount, long? expenseReportId = null)
    {
        var line = new ExpenseReportLine
        {
            CompanyId = companyId,
            UserId = userId,
            Status = expenseReportId is null ? "Loose" : "InReport",
            ExpenseReportId = expenseReportId,
            ExpenseTypeId = expenseTypeId,
            Date = date,
            Amount = reimbursementAmount,
            ReimbursementAmount = reimbursementAmount,
            Currency = "CLP",
        };
        db.ExpenseReportLines.Add(line);
        await db.SaveChangesAsync();
        return line.Id;
    }

    private static async Task<long> AddReportAsync(RendicionesDbContext db, Guid companyId, Guid userId, string status)
    {
        var report = new ExpenseReport
        {
            CompanyId = companyId,
            UserId = userId,
            Status = status,
        };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();
        return report.Id;
    }

    [Fact]
    public async Task NoProfileAssigned_ReturnsEmpty()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(NoProfileAssigned_ReturnsEmpty));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);
        // Política Weekly solo para un perfil específico -- el usuario no tiene perfil asignado.
        await AddWeeklyPolicyAsync(db, companyId, expenseTypeId, policyProfileId: 999, maxAmount: 10000m);
        await AddLineAsync(db, companyId, userId, expenseTypeId, new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero), 50000m);

        var sut = new WeeklyExpenseLimitService(db, new PolicyProfileService(db));
        var result = await sut.GetWarningsAsync(companyId, userId, new DateOnly(2026, 10, 28));

        Assert.Empty(result);
    }

    [Fact]
    public async Task ProfileWithoutWeeklyPolicy_ReturnsEmpty()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(ProfileWithoutWeeklyPolicy_ReturnsEmpty));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);
        // Solo hay una política PerDocument, ninguna Weekly.
        db.ExpensePolicies.Add(new ExpensePolicy
        {
            CompanyId = companyId,
            ExpenseTypeId = expenseTypeId,
            Period = ExpensePolicyPeriod.PerDocument,
            MaxAmount = 10000m,
            IsActive = true,
        });
        await db.SaveChangesAsync();
        await AddLineAsync(db, companyId, userId, expenseTypeId, new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero), 50000m);

        var sut = new WeeklyExpenseLimitService(db, new PolicyProfileService(db));
        var result = await sut.GetWarningsAsync(companyId, userId, new DateOnly(2026, 10, 28));

        Assert.Empty(result);
    }

    [Fact]
    public async Task UnderCap_ReturnsEmpty()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(UnderCap_ReturnsEmpty));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);
        await AddWeeklyPolicyAsync(db, companyId, expenseTypeId, policyProfileId: null, maxAmount: 10000m);
        // Miércoles 2026-10-28 -- semana lunes 2026-10-26 a domingo 2026-11-01.
        await AddLineAsync(db, companyId, userId, expenseTypeId, new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero), 4000m);
        await AddLineAsync(db, companyId, userId, expenseTypeId, new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero), 3000m);

        var sut = new WeeklyExpenseLimitService(db, new PolicyProfileService(db));
        var result = await sut.GetWarningsAsync(companyId, userId, new DateOnly(2026, 10, 28));

        Assert.Empty(result);
    }

    [Fact]
    public async Task OverCap_ReturnsWarningWithCorrectExcess()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(OverCap_ReturnsWarningWithCorrectExcess));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId, "Almuerzo");
        await AddWeeklyPolicyAsync(db, companyId, expenseTypeId, policyProfileId: null, maxAmount: 10000m);
        await AddLineAsync(db, companyId, userId, expenseTypeId, new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), 6000m);
        await AddLineAsync(db, companyId, userId, expenseTypeId, new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero), 7000m);

        var sut = new WeeklyExpenseLimitService(db, new PolicyProfileService(db));
        var result = await sut.GetWarningsAsync(companyId, userId, new DateOnly(2026, 10, 28));

        var warning = Assert.Single(result);
        Assert.Equal("Almuerzo", warning.ExpenseTypeName);
        Assert.Equal(new DateOnly(2026, 10, 26), warning.WeekStart);
        Assert.Equal(new DateOnly(2026, 11, 1), warning.WeekEnd);
        Assert.Equal(13000m, warning.TotalReimbursed);
        Assert.Equal(10000m, warning.MaxAmount);
        Assert.Equal(3000m, warning.Excess);
    }

    [Fact]
    public async Task ExcludesLinesFromRejectedReports()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(ExcludesLinesFromRejectedReports));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);
        await AddWeeklyPolicyAsync(db, companyId, expenseTypeId, policyProfileId: null, maxAmount: 10000m);

        var rejectedReportId = await AddReportAsync(db, companyId, userId, "Rejected");
        // Esta línea sola superaría el tope, pero su informe fue rechazado -- no cuenta.
        await AddLineAsync(db, companyId, userId, expenseTypeId, new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero), 15000m, rejectedReportId);

        var sut = new WeeklyExpenseLimitService(db, new PolicyProfileService(db));
        var result = await sut.GetWarningsAsync(companyId, userId, new DateOnly(2026, 10, 28));

        Assert.Empty(result);
    }

    [Fact]
    public async Task IncludesLooseAndInReportLines()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(IncludesLooseAndInReportLines));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);
        await AddWeeklyPolicyAsync(db, companyId, expenseTypeId, policyProfileId: null, maxAmount: 10000m);

        var pendingReportId = await AddReportAsync(db, companyId, userId, "Pending");
        var approvedReportId = await AddReportAsync(db, companyId, userId, "Approved");

        // Suelta + en informe Pending + en informe Approved, todas dentro de la misma semana.
        await AddLineAsync(db, companyId, userId, expenseTypeId, new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), 4000m);
        await AddLineAsync(db, companyId, userId, expenseTypeId, new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero), 4000m, pendingReportId);
        await AddLineAsync(db, companyId, userId, expenseTypeId, new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero), 4000m, approvedReportId);

        var sut = new WeeklyExpenseLimitService(db, new PolicyProfileService(db));
        var result = await sut.GetWarningsAsync(companyId, userId, new DateOnly(2026, 10, 28));

        var warning = Assert.Single(result);
        Assert.Equal(12000m, warning.TotalReimbursed);
        Assert.Equal(2000m, warning.Excess);
    }

    [Fact]
    public async Task WeekBoundary_MondayToSunday_LinesOutsideWeekNotCounted()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(WeekBoundary_MondayToSunday_LinesOutsideWeekNotCounted));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);
        await AddWeeklyPolicyAsync(db, companyId, expenseTypeId, policyProfileId: null, maxAmount: 5000m);

        // Semana de referencia: lunes 2026-10-26 a domingo 2026-11-01. La línea dentro de la
        // semana ya supera el tope por sí sola (9000 > 5000, excedente 4000). Las líneas fuera
        // de la semana (sábado anterior y lunes siguiente) son enormes a propósito: si el
        // cálculo las contara por error, el total y el excedente cambiarían radicalmente.
        await AddLineAsync(db, companyId, userId, expenseTypeId, new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero), 100000m); // sábado anterior
        await AddLineAsync(db, companyId, userId, expenseTypeId, new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), 9000m); // lunes de la semana
        await AddLineAsync(db, companyId, userId, expenseTypeId, new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), 100000m); // lunes siguiente

        var sut = new WeeklyExpenseLimitService(db, new PolicyProfileService(db));
        var result = await sut.GetWarningsAsync(companyId, userId, new DateOnly(2026, 10, 28));

        var warning = Assert.Single(result);
        Assert.Equal(9000m, warning.TotalReimbursed);
        Assert.Equal(4000m, warning.Excess);
        Assert.Equal(new DateOnly(2026, 10, 26), warning.WeekStart);
        Assert.Equal(new DateOnly(2026, 11, 1), warning.WeekEnd);
    }
}
