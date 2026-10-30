using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

/// <summary>
/// Revisión final de la rama (I1, I2, 2026-10-29): cubre directamente
/// ExpensePolicyService.ValidateAsync -- antes el cálculo confiaba ciegamente en la
/// cantidad que mandaba el llamador (el formulario solo oculta el campo Cantidad con
/// display:none al cambiar de tipo, nunca lo limpia, así que un valor obsoleto seguía
/// llegando) y el mensaje de advertencia no identificaba el tipo/ciudad. Los escenarios
/// de integración completos (vía ExpenseService.CreateAsync) siguen en
/// ExpenseServiceReimbursementTests.
/// </summary>
public class ExpensePolicyServiceValidateAsyncTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    private static async Task<long> CreateExpenseTypeAsync(RendicionesDbContext db, Guid companyId, string name,
        bool requiresQuantity = false)
    {
        var type = new ExpenseType { CompanyId = companyId, Name = name, RequiresQuantity = requiresQuantity };
        db.ExpenseTypes.Add(type);
        await db.SaveChangesAsync();
        return type.Id;
    }

    private static async Task<long> CreateCityAsync(RendicionesDbContext db, Guid companyId, string name)
    {
        var city = new City { CompanyId = companyId, Name = name };
        db.Cities.Add(city);
        await db.SaveChangesAsync();
        return city.Id;
    }

    [Fact]
    public async Task ValidateAsync_TypeDoesNotRequireQuantity_IgnoresStaleQuantityFromCaller()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(ValidateAsync_TypeDoesNotRequireQuantity_IgnoresStaleQuantityFromCaller));
        var typeId = await CreateExpenseTypeAsync(db, companyId, "Almuerzo", requiresQuantity: false);
        var policies = new ExpensePolicyService(db, new PolicyProfileService(db));
        await policies.CreateAsync(companyId, typeId, null, null, ExpensePolicyPeriod.PerDocument, 2000m);

        // Cantidad obsoleta de 3, dejada en el formulario por un tipo anterior -- NO debe
        // multiplicar el tope de un tipo que no requiere cantidad. Si el bug reviviera, el
        // tope sería 2000*3=6000 y 5000 jamás lo superaría.
        var result = await policies.ValidateAsync(companyId, userId, typeId, cityId: null, amount: 5000m,
            quantity: 3m, documentNumber: null, supplierTaxId: null, expenseIdToExclude: null);

        Assert.Equal(2000m, result.ReimbursementAmount);
        Assert.Single(result.Warnings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ValidateAsync_NonPositiveQuantityOnTypeThatRequiresIt_TreatedAsOne(decimal badQuantity)
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb($"{nameof(ValidateAsync_NonPositiveQuantityOnTypeThatRequiresIt_TreatedAsOne)}_{(badQuantity < 0 ? "neg" : "zero")}");
        var typeId = await CreateExpenseTypeAsync(db, companyId, "Movilización (Bip)", requiresQuantity: true);
        var policies = new ExpensePolicyService(db, new PolicyProfileService(db));
        await policies.CreateAsync(companyId, typeId, null, null, ExpensePolicyPeriod.PerDocument, 1000m);

        var result = await policies.ValidateAsync(companyId, userId, typeId, cityId: null, amount: 1500m,
            quantity: badQuantity, documentNumber: null, supplierTaxId: null, expenseIdToExclude: null);

        // Nunca negativo, nunca mayor al tope real (1000*1) -- un tope negativo habría
        // producido un ReimbursementAmount negativo, que infla el saldo de un fondo por
        // rendir y queda mal en el export a SAP.
        Assert.Equal(1000m, result.ReimbursementAmount);
        Assert.True(result.ReimbursementAmount >= 0);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task ValidateAsync_NeverReturnsReimbursementAboveAmount_EvenWithWeirdInputs()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(ValidateAsync_NeverReturnsReimbursementAboveAmount_EvenWithWeirdInputs));
        var typeId = await CreateExpenseTypeAsync(db, companyId, "Alojamiento");
        var policies = new ExpensePolicyService(db, new PolicyProfileService(db));
        // Sin política configurada -- reimbursement debe quedar igual a amount, nunca más.
        var result = await policies.ValidateAsync(companyId, userId, typeId, cityId: null, amount: 100m,
            quantity: 1m, documentNumber: null, supplierTaxId: null, expenseIdToExclude: null);

        Assert.Equal(100m, result.ReimbursementAmount);
    }

    /// <summary>
    /// I1b: aunque el formulario de PoliticasGasto ahora valida [Range(0, ...)] en el monto
    /// máximo, el clamp final de ValidateAsync es el backstop real -- una fila con
    /// MaxAmount negativo (ej. cargada antes del fix, o por otra vía) nunca debe producir un
    /// ReimbursementAmount negativo.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_NegativeMaxAmountOnPolicy_NeverProducesNegativeReimbursement()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(ValidateAsync_NegativeMaxAmountOnPolicy_NeverProducesNegativeReimbursement));
        var typeId = await CreateExpenseTypeAsync(db, companyId, "Alojamiento");
        db.ExpensePolicies.Add(new ExpensePolicy
        {
            CompanyId = companyId, ExpenseTypeId = typeId, Period = ExpensePolicyPeriod.PerDocument,
            MaxAmount = -1000m, IsActive = true,
        });
        await db.SaveChangesAsync();
        var policies = new ExpensePolicyService(db, new PolicyProfileService(db));

        var result = await policies.ValidateAsync(companyId, userId, typeId, cityId: null, amount: 500m,
            quantity: 1m, documentNumber: null, supplierTaxId: null, expenseIdToExclude: null);

        Assert.True(result.ReimbursementAmount >= 0);
        Assert.Equal(0m, result.ReimbursementAmount);
    }

    [Fact]
    public async Task ValidateAsync_OverCap_WarningNamesExpenseTypeAndCity()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(ValidateAsync_OverCap_WarningNamesExpenseTypeAndCity));
        var typeId = await CreateExpenseTypeAsync(db, companyId, "Almuerzo");
        var cityId = await CreateCityAsync(db, companyId, "Santiago");
        var policies = new ExpensePolicyService(db, new PolicyProfileService(db));
        await policies.CreateAsync(companyId, typeId, cityId, null, ExpensePolicyPeriod.PerDocument, 6000m);

        var result = await policies.ValidateAsync(companyId, userId, typeId, cityId, amount: 8500m,
            quantity: 1m, documentNumber: null, supplierTaxId: null, expenseIdToExclude: null);

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("Almuerzo", warning);
        Assert.Contains("Santiago", warning);
    }

    [Fact]
    public async Task ValidateAsync_OverCap_NoCity_WarningStillNamesExpenseTypeWithoutCity()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(ValidateAsync_OverCap_NoCity_WarningStillNamesExpenseTypeWithoutCity));
        var typeId = await CreateExpenseTypeAsync(db, companyId, "Combustible");
        var policies = new ExpensePolicyService(db, new PolicyProfileService(db));
        await policies.CreateAsync(companyId, typeId, null, null, ExpensePolicyPeriod.PerDocument, 10000m);

        var result = await policies.ValidateAsync(companyId, userId, typeId, cityId: null, amount: 15000m,
            quantity: 1m, documentNumber: null, supplierTaxId: null, expenseIdToExclude: null);

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("Combustible", warning);
    }
}
