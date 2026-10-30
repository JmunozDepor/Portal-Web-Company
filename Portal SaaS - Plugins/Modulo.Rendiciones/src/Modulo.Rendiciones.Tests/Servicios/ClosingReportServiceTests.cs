using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Tests.Servicios;

/// <summary>
/// Task 9: ClosingReportLine debe llevar tanto Amount (lo que decía la boleta, visible
/// para quien reciba el export) como ReimbursementAmount (lo efectivamente
/// pagado/contabilizado, Task 7) -- no solo Amount.
/// </summary>
public class ClosingReportServiceTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
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

    [Fact]
    public async Task GetLinesAsync_ClosingReportLine_IncludesBothAmountAndReimbursementAmount()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(GetLinesAsync_ClosingReportLine_IncludesBothAmountAndReimbursementAmount));

        var type = new ExpenseType { CompanyId = companyId, Name = "Alojamiento" };
        db.ExpenseTypes.Add(type);
        await db.SaveChangesAsync();

        var resolvedAt = DateTimeOffset.UtcNow;
        var report = new ExpenseReport
        {
            CompanyId = companyId,
            UserId = userId,
            Status = "Approved",
            ResolvedAt = resolvedAt,
        };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();

        // La boleta decía 10000 pero la política topó el reembolso a 6000 -- ambos valores
        // deben llegar al export (Amount para que el auditor vea la boleta original,
        // ReimbursementAmount porque es lo que realmente se paga/contabiliza).
        db.ExpenseReportLines.Add(new ExpenseReportLine
        {
            CompanyId = companyId,
            UserId = userId,
            ExpenseReportId = report.Id,
            ExpenseTypeId = type.Id,
            Status = "InReport",
            Amount = 10000m,
            ReimbursementAmount = 6000m,
            Currency = "CLP",
            Date = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var sut = new ClosingReportService(db, new NoopTenantUserAdminService());
        var (start, end) = ClosingReportService.PeriodBoundsUtc(resolvedAt.Date.AddDays(-1), resolvedAt.Date.AddDays(1));
        var lines = await sut.GenerateAsync(companyId, start, end);

        var line = Assert.Single(lines);
        Assert.Equal(10000m, line.Amount);
        Assert.Equal(6000m, line.ReimbursementAmount);
    }
}
