using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using Modulo.Rendiciones.Tests.Fakes;

namespace Modulo.Rendiciones.Tests.Servicios;

/// <summary>
/// AttachExpensesAsync exige, además de la categoría (ya cubierta en la práctica por el
/// flujo de OCR), que todo gasto tenga su comprobante adjunto antes de poder agregarse a
/// un informe -- sea cual sea su origen (manual, OCR o import por Excel). Ver el pedido
/// del dueño del proyecto: la boleta se digita por el valor real igual, pero siempre debe
/// quedar la imagen asociada antes de que el gasto avance a un informe.
/// </summary>
public class ExpenseReportServiceAttachExpensesTests
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
        public Task<IReadOnlyList<ExpenseFund>> ListByUserAsync(Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
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
    public async Task AttachExpensesAsync_rechaza_un_gasto_sin_comprobante_adjunto()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var db = CreateDb(nameof(AttachExpensesAsync_rechaza_un_gasto_sin_comprobante_adjunto));
        var report = new ExpenseReport { CompanyId = companyId, UserId = userId, Status = "Draft" };
        db.ExpenseReports.Add(report);
        var gasto = new ExpenseReportLine
        {
            CompanyId = companyId, UserId = userId, Status = "Loose", ExpenseTypeId = 1,
            Amount = 1000, Currency = "CLP", ExpenseReceiptId = null,
        };
        db.ExpenseReportLines.Add(gasto);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.AttachExpensesAsync(report.Id, companyId, new[] { gasto.Id }));
        Assert.Contains("comprobante", ex.Message, StringComparison.OrdinalIgnoreCase);

        var reloaded = await db.ExpenseReportLines.FirstAsync(g => g.Id == gasto.Id);
        Assert.Equal("Loose", reloaded.Status);
    }

    [Fact]
    public async Task AttachExpensesAsync_acepta_un_gasto_con_comprobante_adjunto()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var db = CreateDb(nameof(AttachExpensesAsync_acepta_un_gasto_con_comprobante_adjunto));
        var report = new ExpenseReport { CompanyId = companyId, UserId = userId, Status = "Draft" };
        db.ExpenseReports.Add(report);
        var gasto = new ExpenseReportLine
        {
            CompanyId = companyId, UserId = userId, Status = "Loose", ExpenseTypeId = 1,
            Amount = 1000, Currency = "CLP", ExpenseReceiptId = 99,
        };
        db.ExpenseReportLines.Add(gasto);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.AttachExpensesAsync(report.Id, companyId, new[] { gasto.Id });

        var reloaded = await db.ExpenseReportLines.FirstAsync(g => g.Id == gasto.Id);
        Assert.Equal("InReport", reloaded.Status);
        Assert.Equal(report.Id, reloaded.ExpenseReportId);
    }
}
