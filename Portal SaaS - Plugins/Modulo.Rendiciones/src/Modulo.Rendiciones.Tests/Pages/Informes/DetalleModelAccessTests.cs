using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Pages.Informes;
using Modulo.Rendiciones.Servicios;
using Modulo.Rendiciones.Tests.Fakes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Tests.Pages.Informes;

/// <summary>
/// Bug real reportado en producción (2026-10-26): al aprobar un informe, el aprobador
/// quedaba con un 404 justo al redirigir de vuelta a la misma página. Causa:
/// DetalleModel.LoadAsync solo consideraba "aprobador" a quien figura en el NIVEL
/// ACTUAL mientras el informe sigue Pending -- apenas ApproveAsync avanza de nivel (o
/// cierra el informe en Approved), quien acaba de aprobar deja de cumplir esa condición
/// y el chequeo de acceso lo bloquea a él mismo. El fix agrega "ya actuó alguna vez
/// sobre este informe" (ExpenseReportAction.UserId) como otro criterio válido.
/// </summary>
public class DetalleModelAccessTests
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

        public Task<IReadOnlyList<ExpenseApprovalGroup>> ListAsync(Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseApprovalGroup?> GetAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseApprovalGroup?> GetUserGroupAsync(Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ExpenseApprovalGroupMember>> ListMembersAsync(long groupId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<int, Guid>> GetLevelsAsync(long groupId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, Guid>>(new Dictionary<int, Guid> { [1] = Level1Approver });
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

    private sealed class NoopExpenseService : IExpenseService
    {
        public Task<IReadOnlyList<ExpenseReportLine>> ListLooseAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExpenseReportLine>>(Array.Empty<ExpenseReportLine>());
        public Task<IReadOnlyList<ExpenseReportLine>> ListAllAsync(Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseReportLine?> GetAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseSavedResult> CreateAsync(ExpenseReportLine expense, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> UpdateAsync(long id, Guid companyId, ExpenseReportLine data, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveReceiptAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NoopUserCostCenterService : IUserCostCenterService
    {
        public Task<IReadOnlyList<UserCostCenter>> ListAssignedAsync(Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AssignAsync(Guid companyId, Guid userId, string costCenterCode, string? costCenterName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<Guid, int>> CountAssignedByUserAsync(Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CostCenterDto>> GetAvailableAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CostCenterDto>>(Array.Empty<CostCenterDto>());
    }

    private sealed class NoopTenantUserAdminService : ITenantUserAdminService
    {
        public Task<IReadOnlyList<TenantUserDto>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantUserDto>>(Array.Empty<TenantUserDto>());
        public Task<TenantUserDetailDto?> GetAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TenantUserOperationResult> CreateAsync(string username, string email, bool isAdmin, string? fullName = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TenantUserOperationResult> UpdateAsync(Guid userId, string email, bool isAdmin, bool isActive, bool isLocked, string? fullName = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TenantUserOperationResult> DeleteAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CompanyOptionDto>> ListCompaniesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<MenuGroupOptionDto>> ListMenuGroupsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ProfileOptionDto>> ListProfilesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<LeafMenuDto>> ListLeafMenusAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<MenuTreeNodeDto>> ListMenuTreeAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<UserPermissionsDto?> GetPermissionsAsync(Guid userId, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TenantUserOperationResult> SavePermissionsAsync(Guid userId, Guid companyId, IReadOnlyList<long> menuGroupIds, IReadOnlyDictionary<long, long?> profileByMenu, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TenantUserOperationResult> SetDefaultCompanyAsync(Guid userId, Guid? companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TenantUserOperationResult> SetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default) => throw new NotImplementedException();
        public IReadOnlyList<string> ValidatePassword(string password) => throw new NotImplementedException();
        public Task<TenantUserPasswordResult> GenerateAndSetPasswordAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class StubWeeklyExpenseLimitService : IWeeklyExpenseLimitService
    {
        public IReadOnlyList<WeeklyLimitWarning> Warnings { get; set; } = Array.Empty<WeeklyLimitWarning>();

        /// <summary>Registra TODAS las llamadas (C1: LoadAsync ahora llama una vez por semana
        /// distinta entre las líneas del informe, no una sola vez con "hoy").</summary>
        public List<(Guid CompanyId, Guid UserId, DateOnly AnyDateInWeek)> Calls { get; } = new();

        public Task<IReadOnlyList<WeeklyLimitWarning>> GetWarningsAsync(Guid companyId, Guid userId, DateOnly anyDateInWeek,
            CancellationToken ct = default)
        {
            Calls.Add((companyId, userId, anyDateInWeek));
            return Task.FromResult(Warnings);
        }
    }

    private sealed class NoopRoleService : IRendicionesUserRoleService
    {
        public Task<bool> HasRoleAsync(Guid companyId, Guid userId, string role, bool isPlatformAdmin, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> HasAnyRoleAsync(Guid companyId, Guid userId, IReadOnlyList<string> roles, bool isPlatformAdmin, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<string>> ListRolesAsync(Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ListAllAsync(Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetRoleAsync(Guid companyId, Guid userId, string role, bool granted, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ApplyRoleChangesAsync(Guid companyId, IReadOnlyCollection<(Guid UserId, string Role, bool Granted)> changes, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeCurrentUser : ICurrentUserContext
    {
        public FakeCurrentUser(Guid userId) { UserId = userId; }
        public Guid UserId { get; }
        public string Username => "test";
        public bool IsAdmin => false;
        public Guid OrganizationId => Guid.NewGuid();
        public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class FakeCurrentCompany : ICurrentCompanyAccessor
    {
        public FakeCurrentCompany(Guid companyId) { CompanyId = companyId; }
        public Guid CompanyId { get; }
        public string Code => "TEST";
        public string Database => "test";
        public string ServiceLayerUrl => "";
        public string Country => "CL";
        public bool HasCompany => true;
    }

    [Fact]
    public async Task OnGetAsync_el_aprobador_que_recien_aprobo_el_ultimo_nivel_puede_seguir_viendo_el_informe()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approverId = Guid.NewGuid();

        await using var db = CreateDb(nameof(OnGetAsync_el_aprobador_que_recien_aprobo_el_ultimo_nivel_puede_seguir_viendo_el_informe));
        var report = new ExpenseReport
        {
            CompanyId = companyId, UserId = requesterId, Status = "Pending",
            ExpenseApprovalGroupId = 1, CurrentLevel = 1,
        };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();

        var groups = new StubApprovalGroupService { Level1Approver = approverId };
        var reports = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(),
            new FakeEmailSenderService(), new FakeUserContactLookupService(), new FakeNotificationService(), NullLogger<ExpenseReportService>.Instance);

        // El último nivel aprueba -> el informe pasa a Approved, CurrentLevel queda null.
        await reports.ApproveAsync(report.Id, companyId, approverId, comment: null);

        var currentUser = new FakeCurrentUser(approverId);
        var currentCompany = new FakeCurrentCompany(companyId);
        var model = new DetalleModel(reports, new NoopFundService(), new NoopExpenseService(), new NoopUserCostCenterService(),
            groups, new NoopTenantUserAdminService(), new StubWeeklyExpenseLimitService(), new NoopRoleService(), currentUser, currentCompany);

        var result = await model.OnGetAsync(report.Id, CancellationToken.None);

        Assert.IsNotType<NotFoundResult>(result);
        Assert.Equal("Approved", model.Report.Status);
    }

    /// <summary>
    /// Task 11: DetalleModel.WeeklyWarnings es pura cablería sobre
    /// IWeeklyExpenseLimitService.GetWarningsAsync -- no vuelve a probar la lógica del
    /// Task 8, solo que LoadAsync la invoca con el dueño del informe (no con el usuario
    /// logueado, que puede ser el aprobador) y expone lo que el servicio devuelve.
    /// </summary>
    [Fact]
    public async Task OnGetAsync_pobla_WeeklyWarnings_con_lo_que_devuelve_el_servicio_semanal()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();

        await using var db = CreateDb(nameof(OnGetAsync_pobla_WeeklyWarnings_con_lo_que_devuelve_el_servicio_semanal));
        var report = new ExpenseReport { CompanyId = companyId, UserId = requesterId, Status = "Draft" };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();
        db.ExpenseReportLines.Add(new ExpenseReportLine
        {
            CompanyId = companyId, UserId = requesterId, ExpenseReportId = report.Id, Status = "InReport",
            Date = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero), Amount = 1000m, ReimbursementAmount = 1000m, Currency = "CLP",
        });
        await db.SaveChangesAsync();

        var groups = new StubApprovalGroupService();
        var reports = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(),
            new FakeEmailSenderService(), new FakeUserContactLookupService(), new FakeNotificationService(), NullLogger<ExpenseReportService>.Instance);

        var expectedWarning = new WeeklyLimitWarning("Viáticos", new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 6), 150000m, 100000m, 50000m);
        var weeklyLimits = new StubWeeklyExpenseLimitService { Warnings = new[] { expectedWarning } };

        var currentUser = new FakeCurrentUser(requesterId);
        var currentCompany = new FakeCurrentCompany(companyId);
        var model = new DetalleModel(reports, new NoopFundService(), new NoopExpenseService(), new NoopUserCostCenterService(),
            groups, new NoopTenantUserAdminService(), weeklyLimits, new NoopRoleService(), currentUser, currentCompany);

        var result = await model.OnGetAsync(report.Id, CancellationToken.None);

        Assert.IsNotType<NotFoundResult>(result);
        Assert.Single(model.WeeklyWarnings);
        Assert.Same(expectedWarning, model.WeeklyWarnings[0]);

        // C1: debe llamarse con el lunes de la semana EN QUE CAE LA LÍNEA del informe
        // (2026-08-31, la semana de un gasto de meses atrás) -- nunca con "hoy".
        var call = Assert.Single(weeklyLimits.Calls);
        Assert.Equal(companyId, call.CompanyId);
        Assert.Equal(requesterId, call.UserId);
        Assert.Equal(new DateOnly(2026, 8, 31), call.AnyDateInWeek);
    }
}
