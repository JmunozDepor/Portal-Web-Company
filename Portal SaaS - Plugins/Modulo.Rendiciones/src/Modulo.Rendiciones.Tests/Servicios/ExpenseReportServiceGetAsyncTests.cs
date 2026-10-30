using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using Modulo.Rendiciones.Tests.Fakes;

namespace Modulo.Rendiciones.Tests.Servicios;

/// <summary>
/// I2 (revisión final, 2026-10-29): Informes/Detalle.cshtml necesita mostrarle al auditor
/// la Ciudad de cada línea, pero ExpenseReportService.GetAsync no la incluía -- quedaba
/// null aunque la línea sí tuviera CityId, porque EF Core no trae navegaciones no
/// incluidas explícitamente. Este test solo cubre ese Include -- el resto de
/// GetAsync (ExpenseType/DocumentType) ya estaba cubierto indirectamente por otros tests.
/// </summary>
public class ExpenseReportServiceGetAsyncTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    private sealed class NoopApprovalGroupService : IExpenseApprovalGroupService
    {
        public Task<IReadOnlyList<ExpenseApprovalGroup>> ListAsync(Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseApprovalGroup?> GetAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseApprovalGroup?> GetUserGroupAsync(Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ExpenseApprovalGroupMember>> ListMembersAsync(long groupId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<int, Guid>> GetLevelsAsync(long groupId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CreateAsync(Guid companyId, string name, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddMemberAsync(long groupId, Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveMemberAsync(long groupId, Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetLevelAsync(long groupId, Guid companyId, int level, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveLevelAsync(long groupId, Guid companyId, int level, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NoopFundService : IExpenseFundService
    {
        public Task<IReadOnlyList<ExpenseFund>> ListByUserAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExpenseFund>>(Array.Empty<ExpenseFund>());
        public Task<long> CreateAsync(ExpenseFund fund, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<decimal> CalculatePendingBalanceAsync(long fundId, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RecalculateStatusAsync(long fundId, Guid companyId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopAttachmentService : IAttachmentStorageService
    {
        public Task<long> SaveAsync(Guid companyId, Guid userId, string fileName, string mimeType, byte[] content, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseReceipt?> GetAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ExpenseReportService CreateSut(RendicionesDbContext db) =>
        new(db, new NoopApprovalGroupService(), new NoopFundService(), new NoopAttachmentService(),
            new FakeEmailSenderService(), new FakeUserContactLookupService(), new FakeNotificationService(), NullLogger<ExpenseReportService>.Instance);

    [Fact]
    public async Task GetAsync_IncludesCityOnEachLine()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(GetAsync_IncludesCityOnEachLine));

        var city = new City { CompanyId = companyId, Name = "Antofagasta" };
        db.Cities.Add(city);
        await db.SaveChangesAsync();

        var report = new ExpenseReport { CompanyId = companyId, UserId = userId, Status = "Draft" };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();

        db.ExpenseReportLines.Add(new ExpenseReportLine
        {
            CompanyId = companyId, UserId = userId, ExpenseReportId = report.Id, Status = "InReport",
            Date = DateTimeOffset.UtcNow, Amount = 1000m, ReimbursementAmount = 1000m, Currency = "CLP",
            CityId = city.Id,
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var reloaded = await sut.GetAsync(report.Id, companyId);

        Assert.NotNull(reloaded);
        var line = Assert.Single(reloaded!.Lines);
        Assert.NotNull(line.City);
        Assert.Equal("Antofagasta", line.City!.Name);
    }
}
