using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using Modulo.Rendiciones.Tests.Fakes;

namespace Modulo.Rendiciones.Tests.Servicios;

/// <summary>
/// ListResolvedByApproverAsync alimenta la pestaña "Resueltas por mí" de
/// Pages/Aprobaciones/Index -- pedido explícito tras el bug real de 2026-10-26 donde
/// un aprobador que ya actuó dejaba de "ver" el informe (ver
/// DetalleModelAccessTests). Acá el pedido es más simple: una lista de historial que
/// NO desaparece cuando el informe avanza a otro nivel o se cierra.
/// </summary>
public class ExpenseReportServiceResolvedByApproverTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    private sealed class StubApprovalGroupService : IExpenseApprovalGroupService
    {
        public Guid Level1Approver;
        public Guid Level2Approver;

        public Task<IReadOnlyList<ExpenseApprovalGroup>> ListAsync(Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseApprovalGroup?> GetAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseApprovalGroup?> GetUserGroupAsync(Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ExpenseApprovalGroupMember>> ListMembersAsync(long groupId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<int, Guid>> GetLevelsAsync(long groupId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, Guid>>(new Dictionary<int, Guid> { [1] = Level1Approver, [2] = Level2Approver });
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

    private static ExpenseReportService CreateSut(RendicionesDbContext db, StubApprovalGroupService groups) =>
        new(db, groups, new NoopFundService(), new NoopAttachmentService(),
            new FakeEmailSenderService(), new FakeUserContactLookupService(), new FakeNotificationService(), NullLogger<ExpenseReportService>.Instance);

    [Fact]
    public async Task ListResolvedByApproverAsync_incluye_un_informe_que_el_aprobador_de_nivel_1_ya_aprobo_aunque_siga_pendiente_en_nivel_2()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approver1Id = Guid.NewGuid();
        var approver2Id = Guid.NewGuid();

        await using var db = CreateDb(nameof(ListResolvedByApproverAsync_incluye_un_informe_que_el_aprobador_de_nivel_1_ya_aprobo_aunque_siga_pendiente_en_nivel_2));
        var report = new ExpenseReport
        {
            CompanyId = companyId, UserId = requesterId, Status = "Pending",
            ExpenseApprovalGroupId = 1, CurrentLevel = 1,
        };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();

        var groups = new StubApprovalGroupService { Level1Approver = approver1Id, Level2Approver = approver2Id };
        var sut = CreateSut(db, groups);

        await sut.ApproveAsync(report.Id, companyId, approver1Id, comment: null);

        var resolved = await sut.ListResolvedByApproverAsync(companyId, approver1Id);

        var item = Assert.Single(resolved);
        Assert.Equal(report.Id, item.Report.Id);
        Assert.Equal("Pending", item.Report.Status);
        Assert.Equal(2, item.Report.CurrentLevel);
        Assert.Equal("Approved", item.MyLastAction.Decision);
        Assert.Equal(1, item.MyLastAction.Level);

        // El aprobador de nivel 2 todavía no actuó -- no debe aparecer en SU historial.
        Assert.Empty(await sut.ListResolvedByApproverAsync(companyId, approver2Id));
    }

    [Fact]
    public async Task ListResolvedByApproverAsync_vacio_si_nunca_actuo(){
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(ListResolvedByApproverAsync_vacio_si_nunca_actuo));
        var sut = CreateSut(db, new StubApprovalGroupService());

        var resolved = await sut.ListResolvedByApproverAsync(companyId, Guid.NewGuid());

        Assert.Empty(resolved);
    }
}
