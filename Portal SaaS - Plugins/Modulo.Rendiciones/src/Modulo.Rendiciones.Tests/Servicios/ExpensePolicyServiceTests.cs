using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

/// <summary>
/// ExpensePolicy ganó CityId/PolicyProfileId/Period (Task 5): puede haber varias filas por
/// tipo de gasto (una "default" sin ciudad/perfil, y otras más específicas). IsBlocking
/// desapareció del modelo -- todo tope superado es ahora solo una advertencia (eso lo
/// cubre ExpenseServiceTests/validación, no este archivo). Este archivo cubre
/// exclusivamente el CRUD: alta con combinación (tipo, ciudad, perfil), el guard de
/// duplicados a nivel de servicio, y que Weekly fuerza CityId a null.
/// </summary>
public class ExpensePolicyServiceTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    private static async Task<long> CreateExpenseTypeAsync(RendicionesDbContext db, Guid companyId, string name = "Alojamiento")
    {
        var type = new ExpenseType { CompanyId = companyId, Name = name };
        db.ExpenseTypes.Add(type);
        await db.SaveChangesAsync();
        return type.Id;
    }

    [Fact]
    public async Task CreateAsync_DefaultCityAndProfile_Persists()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_DefaultCityAndProfile_Persists));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);
        var sut = new ExpensePolicyService(db, new PolicyProfileService(db));

        var id = await sut.CreateAsync(companyId, expenseTypeId, cityId: null, policyProfileId: null,
            period: ExpensePolicyPeriod.PerDocument, maxAmount: 15000m);

        var all = await sut.ListAsync(companyId);
        Assert.Single(all);
        Assert.Equal(id, all[0].Id);
        Assert.Null(all[0].CityId);
        Assert.Null(all[0].PolicyProfileId);
        Assert.Equal(ExpensePolicyPeriod.PerDocument, all[0].Period);
        Assert.Equal(15000m, all[0].MaxAmount);
        Assert.True(all[0].IsActive);
    }

    [Fact]
    public async Task CreateAsync_SameTypeDifferentCity_BothAllowed()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_SameTypeDifferentCity_BothAllowed));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);
        var citySantiago = new City { CompanyId = companyId, Name = "Santiago" };
        var cityConcepcion = new City { CompanyId = companyId, Name = "Concepción" };
        db.Cities.AddRange(citySantiago, cityConcepcion);
        await db.SaveChangesAsync();
        var sut = new ExpensePolicyService(db, new PolicyProfileService(db));

        await sut.CreateAsync(companyId, expenseTypeId, citySantiago.Id, null, ExpensePolicyPeriod.PerDocument, 20000m);
        await sut.CreateAsync(companyId, expenseTypeId, cityConcepcion.Id, null, ExpensePolicyPeriod.PerDocument, 10000m);

        var all = await sut.ListAsync(companyId);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task CreateAsync_DuplicateTypeAndCityAndProfile_ThrowsFriendlyMessage()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_DuplicateTypeAndCityAndProfile_ThrowsFriendlyMessage));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);
        var sut = new ExpensePolicyService(db, new PolicyProfileService(db));
        await sut.CreateAsync(companyId, expenseTypeId, null, null, ExpensePolicyPeriod.PerDocument, 15000m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.CreateAsync(companyId, expenseTypeId, null, null, ExpensePolicyPeriod.PerDocument, 20000m));

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public async Task CreateAsync_WeeklyPeriod_IgnoresCityId()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_WeeklyPeriod_IgnoresCityId));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);
        var city = new City { CompanyId = companyId, Name = "Santiago" };
        db.Cities.Add(city);
        await db.SaveChangesAsync();
        var sut = new ExpensePolicyService(db, new PolicyProfileService(db));

        var id = await sut.CreateAsync(companyId, expenseTypeId, city.Id, null, ExpensePolicyPeriod.Weekly, 50000m);

        var all = await sut.ListAsync(companyId);
        var created = Assert.Single(all, p => p.Id == id);
        Assert.Null(created.CityId);
        Assert.Equal(ExpensePolicyPeriod.Weekly, created.Period);
    }

    [Fact]
    public async Task UpdateAsync_NoLongerAcceptsIsBlocking()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(UpdateAsync_NoLongerAcceptsIsBlocking));
        var expenseTypeId = await CreateExpenseTypeAsync(db, companyId);
        var sut = new ExpensePolicyService(db, new PolicyProfileService(db));
        var id = await sut.CreateAsync(companyId, expenseTypeId, null, null, ExpensePolicyPeriod.PerDocument, 15000m);

        // Compila sin parámetro isBlocking -- la firma ya no lo acepta.
        await sut.UpdateAsync(id, companyId, maxAmount: 18000m, isActive: false);

        var all = await sut.ListAsync(companyId);
        Assert.Equal(18000m, all[0].MaxAmount);
        Assert.False(all[0].IsActive);
    }
}
