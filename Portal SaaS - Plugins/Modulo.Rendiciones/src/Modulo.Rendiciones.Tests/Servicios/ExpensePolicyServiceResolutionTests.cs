using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

/// <summary>
/// Cubre `IExpensePolicyService.FindApplicablePolicyAsync` (Task 6): dado un tipo de gasto,
/// una ciudad opcional y un perfil de usuario opcional, resuelve cuál fila de ExpensePolicy
/// aplica según el orden de especificidad del spec (Decisión 4) -- 4 niveles de fallback,
/// de más específico a menos. Deliberadamente separado de ExpensePolicyServiceTests.cs
/// (ese archivo cubre el CRUD; este cubre la resolución de lectura).
/// </summary>
public class ExpensePolicyServiceResolutionTests
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

    private static async Task<long> AddPolicyAsync(RendicionesDbContext db, Guid companyId, long expenseTypeId,
        long? cityId, long? policyProfileId, decimal maxAmount, bool isActive = true,
        string period = ExpensePolicyPeriod.PerDocument)
    {
        var policy = new ExpensePolicy
        {
            CompanyId = companyId,
            ExpenseTypeId = expenseTypeId,
            CityId = cityId,
            PolicyProfileId = policyProfileId,
            Period = period,
            MaxAmount = maxAmount,
            IsActive = isActive,
        };
        db.ExpensePolicies.Add(policy);
        await db.SaveChangesAsync();
        return policy.Id;
    }

    [Fact]
    public async Task Level1_CityAndProfileMatch_Wins()
    {
        var companyId = Guid.NewGuid();
        const long citySantiago = 100;
        const long profileSupervisor = 200;
        await using var db = CreateDb(nameof(Level1_CityAndProfileMatch_Wins));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);

        var level1Id = await AddPolicyAsync(db, companyId, expenseTypeId, citySantiago, profileSupervisor, 6000m);
        await AddPolicyAsync(db, companyId, expenseTypeId, citySantiago, null, 5000m);
        await AddPolicyAsync(db, companyId, expenseTypeId, null, profileSupervisor, 4000m);
        await AddPolicyAsync(db, companyId, expenseTypeId, null, null, 3000m);

        var sut = new ExpensePolicyService(db);
        var result = await sut.FindApplicablePolicyAsync(companyId, expenseTypeId, citySantiago, profileSupervisor,
            ExpensePolicyPeriod.PerDocument);

        Assert.NotNull(result);
        Assert.Equal(level1Id, result!.Id);
        Assert.Equal(6000m, result.MaxAmount);
    }

    [Fact]
    public async Task Level2_CityMatchesProfileNull_WinsWhenNoLevel1()
    {
        var companyId = Guid.NewGuid();
        const long citySantiago = 100;
        const long profileSupervisor = 200;
        await using var db = CreateDb(nameof(Level2_CityMatchesProfileNull_WinsWhenNoLevel1));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);

        var level2Id = await AddPolicyAsync(db, companyId, expenseTypeId, citySantiago, null, 5000m);
        await AddPolicyAsync(db, companyId, expenseTypeId, null, profileSupervisor, 4000m);
        await AddPolicyAsync(db, companyId, expenseTypeId, null, null, 3000m);

        var sut = new ExpensePolicyService(db);
        var result = await sut.FindApplicablePolicyAsync(companyId, expenseTypeId, citySantiago, profileSupervisor,
            ExpensePolicyPeriod.PerDocument);

        Assert.NotNull(result);
        Assert.Equal(level2Id, result!.Id);
        Assert.Equal(5000m, result.MaxAmount);
    }

    [Fact]
    public async Task Level3_ProfileMatchesCityNull_WinsWhenNoLevel1Or2()
    {
        var companyId = Guid.NewGuid();
        const long citySantiago = 100;
        const long profileSupervisor = 200;
        await using var db = CreateDb(nameof(Level3_ProfileMatchesCityNull_WinsWhenNoLevel1Or2));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);

        var level3Id = await AddPolicyAsync(db, companyId, expenseTypeId, null, profileSupervisor, 4000m);
        await AddPolicyAsync(db, companyId, expenseTypeId, null, null, 3000m);

        var sut = new ExpensePolicyService(db);
        var result = await sut.FindApplicablePolicyAsync(companyId, expenseTypeId, citySantiago, profileSupervisor,
            ExpensePolicyPeriod.PerDocument);

        Assert.NotNull(result);
        Assert.Equal(level3Id, result!.Id);
        Assert.Equal(4000m, result.MaxAmount);
    }

    [Fact]
    public async Task Level4_BothNull_WinsWhenNothingElseMatches()
    {
        var companyId = Guid.NewGuid();
        const long citySantiago = 100;
        const long profileSupervisor = 200;
        await using var db = CreateDb(nameof(Level4_BothNull_WinsWhenNothingElseMatches));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);

        var level4Id = await AddPolicyAsync(db, companyId, expenseTypeId, null, null, 3000m);

        var sut = new ExpensePolicyService(db);
        var result = await sut.FindApplicablePolicyAsync(companyId, expenseTypeId, citySantiago, profileSupervisor,
            ExpensePolicyPeriod.PerDocument);

        Assert.NotNull(result);
        Assert.Equal(level4Id, result!.Id);
        Assert.Equal(3000m, result.MaxAmount);
    }

    [Fact]
    public async Task NoPolicyAtAll_ReturnsNull()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(NoPolicyAtAll_ReturnsNull));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);

        var sut = new ExpensePolicyService(db);
        var result = await sut.FindApplicablePolicyAsync(companyId, expenseTypeId, cityId: 100, userProfileId: 200,
            ExpensePolicyPeriod.PerDocument);

        Assert.Null(result);
    }

    [Fact]
    public async Task UserWithoutProfile_OnlyLevels2And4Considered()
    {
        var companyId = Guid.NewGuid();
        const long citySantiago = 100;
        const long profileSupervisor = 200;
        await using var db = CreateDb(nameof(UserWithoutProfile_OnlyLevels2And4Considered));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);

        // Políticas con perfil asignado nunca deben matchear a un usuario sin perfil.
        await AddPolicyAsync(db, companyId, expenseTypeId, citySantiago, profileSupervisor, 6000m);
        await AddPolicyAsync(db, companyId, expenseTypeId, null, profileSupervisor, 4000m);
        var level2Id = await AddPolicyAsync(db, companyId, expenseTypeId, citySantiago, null, 5000m);
        var level4Id = await AddPolicyAsync(db, companyId, expenseTypeId, null, null, 3000m);

        var sut = new ExpensePolicyService(db);

        var withCity = await sut.FindApplicablePolicyAsync(companyId, expenseTypeId, citySantiago,
            userProfileId: null, ExpensePolicyPeriod.PerDocument);
        Assert.NotNull(withCity);
        Assert.Equal(level2Id, withCity!.Id);

        var withoutCity = await sut.FindApplicablePolicyAsync(companyId, expenseTypeId, cityId: null,
            userProfileId: null, ExpensePolicyPeriod.PerDocument);
        Assert.NotNull(withoutCity);
        Assert.Equal(level4Id, withoutCity!.Id);
    }

    [Fact]
    public async Task InactivePolicy_NeverMatches()
    {
        var companyId = Guid.NewGuid();
        const long citySantiago = 100;
        const long profileSupervisor = 200;
        await using var db = CreateDb(nameof(InactivePolicy_NeverMatches));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);

        await AddPolicyAsync(db, companyId, expenseTypeId, citySantiago, profileSupervisor, 6000m, isActive: false);

        var sut = new ExpensePolicyService(db);
        var result = await sut.FindApplicablePolicyAsync(companyId, expenseTypeId, citySantiago, profileSupervisor,
            ExpensePolicyPeriod.PerDocument);

        Assert.Null(result);
    }
}
