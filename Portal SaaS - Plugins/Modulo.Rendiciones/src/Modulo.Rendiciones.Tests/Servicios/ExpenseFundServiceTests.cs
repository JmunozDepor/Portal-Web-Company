using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

/// <summary>
/// Task 9: el saldo pendiente de un fondo por rendir debe descontar lo efectivamente
/// reembolsado (ExpenseReportLine.ReimbursementAmount, Task 7), no lo que decía la
/// boleta (Amount) -- el producto confirmó que el monto a pagar es el de la política
/// cuando es menor al de la boleta.
/// </summary>
public class ExpenseFundServiceTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    private static ExpenseFund NewFund(Guid companyId, Guid userId, decimal amount) => new()
    {
        CompanyId = companyId,
        UserId = userId,
        Currency = "CLP",
        Amount = amount,
    };

    [Fact]
    public async Task CalculatePendingBalanceAsync_UsesReimbursementAmount_NotAmount()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CalculatePendingBalanceAsync_UsesReimbursementAmount_NotAmount));

        var fund = NewFund(companyId, userId, amount: 20000m);
        db.ExpenseFunds.Add(fund);
        await db.SaveChangesAsync();

        var report = new ExpenseReport
        {
            CompanyId = companyId,
            UserId = userId,
            ExpenseFundId = fund.Id,
            Status = "Approved",
        };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();

        // La boleta decía 10000 pero la política topó el reembolso a 6000 -- el saldo
        // pendiente del fondo debe reflejar 6000 restado, no 10000.
        db.ExpenseReportLines.Add(new ExpenseReportLine
        {
            CompanyId = companyId,
            UserId = userId,
            ExpenseReportId = report.Id,
            Status = "InReport",
            Amount = 10000m,
            ReimbursementAmount = 6000m,
            Currency = "CLP",
            Date = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var sut = new ExpenseFundService(db);
        var balance = await sut.CalculatePendingBalanceAsync(fund.Id, companyId);

        Assert.Equal(14000m, balance);
    }

    [Fact]
    public async Task CalculatePendingBalanceAsync_IgnoresLinesFromReportsNotApproved()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CalculatePendingBalanceAsync_IgnoresLinesFromReportsNotApproved));

        var fund = NewFund(companyId, userId, amount: 20000m);
        db.ExpenseFunds.Add(fund);
        await db.SaveChangesAsync();

        var report = new ExpenseReport
        {
            CompanyId = companyId,
            UserId = userId,
            ExpenseFundId = fund.Id,
            Status = "Pending",
        };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();

        db.ExpenseReportLines.Add(new ExpenseReportLine
        {
            CompanyId = companyId,
            UserId = userId,
            ExpenseReportId = report.Id,
            Status = "InReport",
            Amount = 10000m,
            ReimbursementAmount = 6000m,
            Currency = "CLP",
            Date = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var sut = new ExpenseFundService(db);
        var balance = await sut.CalculatePendingBalanceAsync(fund.Id, companyId);

        Assert.Equal(20000m, balance);
    }

    [Fact]
    public async Task CalculatePendingBalanceAsync_ThrowsWhenFundDoesNotExist()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CalculatePendingBalanceAsync_ThrowsWhenFundDoesNotExist));
        var sut = new ExpenseFundService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CalculatePendingBalanceAsync(999, companyId));
    }
}
