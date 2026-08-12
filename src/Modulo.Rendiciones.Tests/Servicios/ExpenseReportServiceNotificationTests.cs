using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using Modulo.Rendiciones.Tests.Fakes;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Tests.Servicios;

public class ExpenseReportServiceNotificationTests
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
        public Task<ExpenseApprovalGroup?> GetUserGroupAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<ExpenseApprovalGroup?>(new ExpenseApprovalGroup { Id = 1, CompanyId = companyId, Name = "Grupo Test" });
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

    [Fact]
    public async Task SubmitAsync_notifica_por_correo_al_aprobador_de_nivel_1()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approver1Id = Guid.NewGuid();

        await using var db = CreateDb(nameof(SubmitAsync_notifica_por_correo_al_aprobador_de_nivel_1));
        var report = new ExpenseReport { CompanyId = companyId, UserId = requesterId, Status = "Draft" };
        db.ExpenseReports.Add(report);
        db.ExpenseReportLines.Add(new ExpenseReportLine
        {
            CompanyId = companyId, UserId = requesterId, Status = "InReport", ExpenseReportId = report.Id,
            Amount = 1000, Currency = "CLP",
        });
        await db.SaveChangesAsync();

        var groups = new StubApprovalGroupService { Level1Approver = approver1Id, Level2Approver = approver1Id };
        var emails = new FakeEmailSenderService();
        var orgId = Guid.NewGuid();
        var contacts = new FakeUserContactLookupService()
            .With(approver1Id, new UserContactDto(orgId, "aprobador1@test.cl", true));

        var sut = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(), emails, contacts, NullLogger<ExpenseReportService>.Instance);

        await sut.SubmitAsync(report.Id, companyId);

        var sent = Assert.Single(emails.Sent);
        Assert.Equal(orgId, sent.OrganizationId);
        Assert.Equal("aprobador1@test.cl", sent.Message.ToEmail);
    }

    [Fact]
    public async Task SubmitAsync_no_notifica_si_el_aprobador_desactivo_las_notificaciones_por_correo()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approver1Id = Guid.NewGuid();

        await using var db = CreateDb(nameof(SubmitAsync_no_notifica_si_el_aprobador_desactivo_las_notificaciones_por_correo));
        var report = new ExpenseReport { CompanyId = companyId, UserId = requesterId, Status = "Draft" };
        db.ExpenseReports.Add(report);
        db.ExpenseReportLines.Add(new ExpenseReportLine
        {
            CompanyId = companyId, UserId = requesterId, Status = "InReport", ExpenseReportId = report.Id,
            Amount = 1000, Currency = "CLP",
        });
        await db.SaveChangesAsync();

        var groups = new StubApprovalGroupService { Level1Approver = approver1Id, Level2Approver = approver1Id };
        var emails = new FakeEmailSenderService();
        var contacts = new FakeUserContactLookupService()
            .With(approver1Id, new UserContactDto(Guid.NewGuid(), "aprobador1@test.cl", false));

        var sut = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(), emails, contacts, NullLogger<ExpenseReportService>.Instance);

        await sut.SubmitAsync(report.Id, companyId);

        Assert.Empty(emails.Sent);
    }

    [Fact]
    public async Task ApproveAsync_con_mas_niveles_notifica_al_siguiente_aprobador()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approver1Id = Guid.NewGuid();
        var approver2Id = Guid.NewGuid();

        await using var db = CreateDb(nameof(ApproveAsync_con_mas_niveles_notifica_al_siguiente_aprobador));
        var report = new ExpenseReport
        {
            CompanyId = companyId, UserId = requesterId, Status = "Pending",
            ExpenseApprovalGroupId = 1, CurrentLevel = 1,
        };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();

        var groups = new StubApprovalGroupService { Level1Approver = approver1Id, Level2Approver = approver2Id };
        var emails = new FakeEmailSenderService();
        var orgId = Guid.NewGuid();
        var contacts = new FakeUserContactLookupService()
            .With(approver2Id, new UserContactDto(orgId, "aprobador2@test.cl", true));

        var sut = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(), emails, contacts, NullLogger<ExpenseReportService>.Instance);

        await sut.ApproveAsync(report.Id, companyId, approver1Id, comment: null);

        var sent = Assert.Single(emails.Sent);
        Assert.Equal("aprobador2@test.cl", sent.Message.ToEmail);
    }

    [Fact]
    public async Task ApproveAsync_ultimo_nivel_notifica_al_dueno_del_informe()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approver1Id = Guid.NewGuid();

        await using var db = CreateDb(nameof(ApproveAsync_ultimo_nivel_notifica_al_dueno_del_informe));
        var report = new ExpenseReport
        {
            CompanyId = companyId, UserId = requesterId, Status = "Pending",
            ExpenseApprovalGroupId = 1, CurrentLevel = 1,
        };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();

        // Nivel 2 == solicitante -> ResolveNextLevel salta ese nivel -> queda Approved.
        var groups = new StubApprovalGroupService { Level1Approver = approver1Id, Level2Approver = requesterId };
        var emails = new FakeEmailSenderService();
        var orgId = Guid.NewGuid();
        var contacts = new FakeUserContactLookupService()
            .With(requesterId, new UserContactDto(orgId, "dueno@test.cl", true));

        var sut = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(), emails, contacts, NullLogger<ExpenseReportService>.Instance);

        await sut.ApproveAsync(report.Id, companyId, approver1Id, comment: null);

        var sent = Assert.Single(emails.Sent);
        Assert.Equal("dueno@test.cl", sent.Message.ToEmail);
    }

    [Fact]
    public async Task RejectAsync_notifica_al_dueno_del_informe()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approver1Id = Guid.NewGuid();

        await using var db = CreateDb(nameof(RejectAsync_notifica_al_dueno_del_informe));
        var report = new ExpenseReport
        {
            CompanyId = companyId, UserId = requesterId, Status = "Pending",
            ExpenseApprovalGroupId = 1, CurrentLevel = 1,
        };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();

        var groups = new StubApprovalGroupService { Level1Approver = approver1Id, Level2Approver = approver1Id };
        var emails = new FakeEmailSenderService();
        var orgId = Guid.NewGuid();
        var contacts = new FakeUserContactLookupService()
            .With(requesterId, new UserContactDto(orgId, "dueno@test.cl", true));

        var sut = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(), emails, contacts, NullLogger<ExpenseReportService>.Instance);

        await sut.RejectAsync(report.Id, companyId, approver1Id, comment: "Falta comprobante");

        var sent = Assert.Single(emails.Sent);
        Assert.Equal("dueno@test.cl", sent.Message.ToEmail);
        Assert.Contains("Falta comprobante", sent.Message.HtmlBody);
    }

    [Fact]
    public async Task SubmitAsync_continua_si_el_envio_de_correo_falla()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approver1Id = Guid.NewGuid();

        await using var db = CreateDb(nameof(SubmitAsync_continua_si_el_envio_de_correo_falla));
        var report = new ExpenseReport { CompanyId = companyId, UserId = requesterId, Status = "Draft" };
        db.ExpenseReports.Add(report);
        db.ExpenseReportLines.Add(new ExpenseReportLine
        {
            CompanyId = companyId, UserId = requesterId, Status = "InReport", ExpenseReportId = report.Id,
            Amount = 1000, Currency = "CLP",
        });
        await db.SaveChangesAsync();

        var groups = new StubApprovalGroupService { Level1Approver = approver1Id, Level2Approver = approver1Id };
        var contacts = new FakeUserContactLookupService()
            .With(approver1Id, new UserContactDto(Guid.NewGuid(), "aprobador1@test.cl", true));

        var sut = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(), new ThrowingEmailSenderService(), contacts, NullLogger<ExpenseReportService>.Instance);

        await sut.SubmitAsync(report.Id, companyId);

        var reloaded = await db.ExpenseReports.FirstAsync(r => r.Id == report.Id);
        Assert.Equal("Pending", reloaded.Status);
    }

    private sealed class ThrowingEmailSenderService : PortalSaas.Abstractions.Contratos.IEmailSenderService
    {
        public Task SendAsync(Guid organizationId, EmailMessage message, CancellationToken ct = default) =>
            throw new InvalidOperationException("Proveedor de correo no configurado (simulado en el test).");
    }
}
