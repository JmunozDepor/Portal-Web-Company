using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Pages.Gastos;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Tests.Pages.Gastos;

/// <summary>
/// Task 10: el formulario del rendidor muestra el combo "Ciudad" solo para los tipos de
/// gasto que tienen al menos una política de ciudad activa (ExpenseTypeIdsWithCityPolicy,
/// calculado en DetalleModel.LoadCatalogsAsync). También cubre el fix de Task 7 cargado
/// acá: OnGetAsync debe prefijar Input.CityId/Input.Quantity desde el gasto existente, o
/// una edición que no toca esos dos campos los pisaría con null al guardar.
/// </summary>
public class DetalleModelCatalogsTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
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

    private sealed class FakeExpenseServiceWithStore : IExpenseService
    {
        public ExpenseReportLine? Stored { get; set; }
        public Task<IReadOnlyList<ExpenseReportLine>> ListLooseAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExpenseReportLine>>(Array.Empty<ExpenseReportLine>());
        public Task<IReadOnlyList<ExpenseReportLine>> ListAllAsync(Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseReportLine?> GetAsync(long id, Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(Stored is not null && Stored.Id == id && Stored.CompanyId == companyId ? Stored : null);
        public Task<ExpenseSavedResult> CreateAsync(ExpenseReportLine expense, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> UpdateAsync(long id, Guid companyId, ExpenseReportLine data, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveReceiptAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NoopDocumentTypeService : IDocumentTypeService
    {
        public Task<IReadOnlyList<DocumentType>> ListActiveAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DocumentType>>(Array.Empty<DocumentType>());
        public Task<IReadOnlyList<DocumentType>> ListAllAsync(Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DocumentType?> FindBySiiCodeAsync(Guid companyId, int siiCode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CreateAsync(Guid companyId, string name, bool appliesTax, decimal taxPercentage, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(long id, Guid companyId, string name, bool appliesTax, decimal taxPercentage, bool isActive, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NoopApprovalGroupService : IExpenseApprovalGroupService
    {
        public Task<IReadOnlyList<ExpenseApprovalGroup>> ListAsync(Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseApprovalGroup?> GetAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseApprovalGroup?> GetUserGroupAsync(Guid companyId, Guid userId, CancellationToken ct = default) => Task.FromResult<ExpenseApprovalGroup?>(null);
        public Task<IReadOnlyList<ExpenseApprovalGroupMember>> ListMembersAsync(long groupId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<int, Guid>> GetLevelsAsync(long groupId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CreateAsync(Guid companyId, string name, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddMemberAsync(long groupId, Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveMemberAsync(long groupId, Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetLevelAsync(long groupId, Guid companyId, int level, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveLevelAsync(long groupId, Guid companyId, int level, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NoopUserCostCenterService : IUserCostCenterService
    {
        public Task<IReadOnlyList<UserCostCenter>> ListAssignedAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UserCostCenter>>(Array.Empty<UserCostCenter>());
        public Task AssignAsync(Guid companyId, Guid userId, string costCenterCode, string? costCenterName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<Guid, int>> CountAssignedByUserAsync(Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CostCenterDto>> GetAvailableAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CostCenterDto>>(Array.Empty<CostCenterDto>());
    }

    private sealed class NoopAttachmentService : IAttachmentStorageService
    {
        public Task<long> SaveAsync(Guid companyId, Guid userId, string fileName, string mimeType, byte[] content, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseReceipt?> GetAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NoopRoutingService : IRoutingService
    {
        public Task<RouteResultDto> CalculateDistanceAsync(Guid companyId, string origin, string destination, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class NoopSupplierHintService : ISupplierHintService
    {
        public Task<SupplierHint?> GetAsync(Guid companyId, string normalizedRut, CancellationToken ct = default) => Task.FromResult<SupplierHint?>(null);
        public Task RegisterAsync(Guid companyId, string? rawRut, string? supplierName, long? expenseTypeId, long? documentTypeId, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class NoopAmountFormatService : IAmountFormatService
    {
        public Task<bool> ShowDecimalsAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<string> FormatAsync(decimal amount, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NoopPolicyProfileService : IPolicyProfileService
    {
        public Task<IReadOnlyList<PolicyProfile>> ListActiveAsync(Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<PolicyProfile>> ListAllAsync(Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CreateAsync(Guid companyId, string name, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(long id, Guid companyId, string name, bool isActive, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long?> GetAssignedProfileIdAsync(Guid companyId, Guid userId, CancellationToken ct = default) => Task.FromResult<long?>(null);
        public Task AssignAsync(Guid companyId, Guid userId, long? policyProfileId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<Guid, PolicyProfile>> ListAssignmentsAsync(Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NoopWeeklyExpenseLimitService : IWeeklyExpenseLimitService
    {
        public Task<IReadOnlyList<WeeklyLimitWarning>> GetWarningsAsync(Guid companyId, Guid userId, DateOnly anyDateInWeek, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WeeklyLimitWarning>>(Array.Empty<WeeklyLimitWarning>());
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

    private static DetalleModel BuildModel(RendicionesDbContext db, Guid companyId, Guid userId, IExpenseService? expenses = null)
    {
        var expenseTypes = new ExpenseTypeService(db);
        var documentTypes = new NoopDocumentTypeService();
        var policyProfiles = new NoopPolicyProfileService();
        var policies = new ExpensePolicyService(db, policyProfiles);
        var cities = new CityService(db);
        var currentUser = new FakeCurrentUser(userId);
        var currentCompany = new FakeCurrentCompany(companyId);

        return new DetalleModel(expenses ?? new NoopExpenseService(), expenseTypes, documentTypes, policies,
            new NoopApprovalGroupService(), new NoopUserCostCenterService(), new NoopAttachmentService(),
            new NoopRoutingService(), new NoopSupplierHintService(), new NoopAmountFormatService(), cities,
            policyProfiles, new NoopWeeklyExpenseLimitService(), new NoopRoleService(), currentUser, currentCompany);
    }

    [Fact]
    public async Task OnGetAsync_ExpenseTypeIdsWithCityPolicy_solo_incluye_tipos_con_politica_de_ciudad_activa()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var db = CreateDb(nameof(OnGetAsync_ExpenseTypeIdsWithCityPolicy_solo_incluye_tipos_con_politica_de_ciudad_activa));

        var tipoConPoliticaCiudad = new ExpenseType { CompanyId = companyId, Name = "Almuerzo" };
        var tipoConPoliticaInactivaDeCiudad = new ExpenseType { CompanyId = companyId, Name = "Hospedaje" };
        var tipoConPoliticaSinCiudad = new ExpenseType { CompanyId = companyId, Name = "Movilización" };
        var tipoSinPolitica = new ExpenseType { CompanyId = companyId, Name = "Varios" };
        db.ExpenseTypes.AddRange(tipoConPoliticaCiudad, tipoConPoliticaInactivaDeCiudad, tipoConPoliticaSinCiudad, tipoSinPolitica);
        await db.SaveChangesAsync();

        var ciudad = new City { CompanyId = companyId, Name = "Santiago" };
        db.Cities.Add(ciudad);
        await db.SaveChangesAsync();

        db.ExpensePolicies.AddRange(
            new ExpensePolicy { CompanyId = companyId, ExpenseTypeId = tipoConPoliticaCiudad.Id, CityId = ciudad.Id, Period = ExpensePolicyPeriod.PerDocument, MaxAmount = 10000, IsActive = true },
            new ExpensePolicy { CompanyId = companyId, ExpenseTypeId = tipoConPoliticaInactivaDeCiudad.Id, CityId = ciudad.Id, Period = ExpensePolicyPeriod.PerDocument, MaxAmount = 20000, IsActive = false },
            new ExpensePolicy { CompanyId = companyId, ExpenseTypeId = tipoConPoliticaSinCiudad.Id, CityId = null, Period = ExpensePolicyPeriod.PerDocument, MaxAmount = 5000, IsActive = true });
        await db.SaveChangesAsync();

        var model = BuildModel(db, companyId, userId);

        var result = await model.OnGetAsync(null, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Contains(tipoConPoliticaCiudad.Id, model.ExpenseTypeIdsWithCityPolicy);
        Assert.DoesNotContain(tipoConPoliticaInactivaDeCiudad.Id, model.ExpenseTypeIdsWithCityPolicy);
        Assert.DoesNotContain(tipoConPoliticaSinCiudad.Id, model.ExpenseTypeIdsWithCityPolicy);
        Assert.DoesNotContain(tipoSinPolitica.Id, model.ExpenseTypeIdsWithCityPolicy);

        Assert.Single(model.Cities);
        Assert.Equal("Santiago", model.Cities[0].Name);
    }

    /// <summary>
    /// Fix cargado de la revisión de Task 7: antes de este fix, OnGetAsync no prefijaba
    /// Input.CityId/Input.Quantity desde el gasto existente -- como OnPostGuardarAsync
    /// persiste lo que venga en Input.CityId/Input.Quantity, editar un gasto que ya
    /// tenía ciudad/cantidad sin tocar esos campos los habría borrado en silencio al
    /// guardar (el formulario los habría mostrado vacíos).
    /// </summary>
    [Fact]
    public async Task OnGetAsync_precarga_CityId_y_Quantity_del_gasto_existente()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var db = CreateDb(nameof(OnGetAsync_precarga_CityId_y_Quantity_del_gasto_existente));

        var tipo = new ExpenseType { CompanyId = companyId, Name = "Movilización (Bip)", RequiresQuantity = true, QuantityLabel = "Días" };
        db.ExpenseTypes.Add(tipo);
        await db.SaveChangesAsync();

        var ciudad = new City { CompanyId = companyId, Name = "Viña del Mar" };
        db.Cities.Add(ciudad);
        await db.SaveChangesAsync();

        var expense = new ExpenseReportLine
        {
            CompanyId = companyId,
            UserId = userId,
            ExpenseTypeId = tipo.Id,
            Status = "Loose",
            Date = DateTimeOffset.UtcNow,
            Amount = 15000,
            Currency = "CLP",
            CityId = ciudad.Id,
            Quantity = 3,
        };

        // Se persiste directo en el db (sin pasar por IExpenseService.CreateAsync, que
        // recalcularía ReimbursementAmount/validaría política) solo para obtener un Id real.
        db.ExpenseReportLines.Add(expense);
        await db.SaveChangesAsync();

        var expenses = new FakeExpenseServiceWithStore { Stored = expense };

        var model = BuildModel(db, companyId, userId, expenses);

        var result = await model.OnGetAsync(expense.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(ciudad.Id, model.Input.CityId);
        Assert.Equal(3, model.Input.Quantity);
    }
}
