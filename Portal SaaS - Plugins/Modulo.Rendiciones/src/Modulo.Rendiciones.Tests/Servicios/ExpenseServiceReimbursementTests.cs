using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

/// <summary>
/// Task 7: ExpenseService.CreateAsync/UpdateAsync ya no bloquean por monto -- calculan
/// ReimbursementAmount a partir de la política PerDocument que resuelve
/// ExpensePolicyService.FindApplicablePolicyAsync (Task 6), multiplicando el tope por
/// Quantity cuando corresponde (Task 2/3). Usa los servicios reales (ExpensePolicyService,
/// PolicyProfileService) contra un DbContext en memoria -- a diferencia de
/// ExpenseServiceTests (que usa NoopPolicyService para aislar el esquema), acá el motor de
/// políticas es justamente lo que se está probando.
/// </summary>
public class ExpenseServiceReimbursementTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    private sealed class NoopAttachmentService : IAttachmentStorageService
    {
        public Task<long> SaveAsync(Guid companyId, Guid userId, string fileName, string mimeType, byte[] content, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseReceipt?> GetAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopRoutingService : IRoutingService
    {
        public Task<RouteResultDto> CalculateDistanceAsync(Guid companyId, string origin, string destination, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class NoopSupplierHintService : ISupplierHintService
    {
        public Task<SupplierHint?> GetAsync(Guid companyId, string normalizedRut, CancellationToken ct = default) =>
            Task.FromResult<SupplierHint?>(null);

        public Task RegisterAsync(Guid companyId, string? rawRut, string? supplierName, long? expenseTypeId, long? documentTypeId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private static ExpenseService CreateSut(RendicionesDbContext db)
    {
        var profiles = new PolicyProfileService(db);
        var policies = new ExpensePolicyService(db, profiles);
        return new ExpenseService(db, new NoopAttachmentService(), policies, new NoopRoutingService(), new NoopSupplierHintService(),
            NullLogger<ExpenseService>.Instance);
    }

    private static async Task<long> CreateExpenseTypeAsync(RendicionesDbContext db, Guid companyId, string name = "Alojamiento",
        bool requiresQuantity = false)
    {
        var type = new ExpenseType { CompanyId = companyId, Name = name, RequiresQuantity = requiresQuantity };
        db.ExpenseTypes.Add(type);
        await db.SaveChangesAsync();
        return type.Id;
    }

    private static ExpenseReportLine NewLine(Guid companyId, Guid userId, long expenseTypeId, decimal amount,
        long? cityId = null, decimal? quantity = null, string? documentNumber = null) => new()
    {
        CompanyId = companyId,
        UserId = userId,
        ExpenseTypeId = expenseTypeId,
        Amount = amount,
        Currency = "CLP",
        Date = DateTimeOffset.UtcNow,
        CityId = cityId,
        Quantity = quantity,
        DocumentNumber = documentNumber,
    };

    [Fact]
    public async Task CreateAsync_AmountUnderCap_ReimbursementEqualsAmount_NoWarning()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_AmountUnderCap_ReimbursementEqualsAmount_NoWarning));
        var typeId = await CreateExpenseTypeAsync(db, companyId);
        var policies = new ExpensePolicyService(db, new PolicyProfileService(db));
        await policies.CreateAsync(companyId, typeId, null, null, ExpensePolicyPeriod.PerDocument, 15000m);
        var sut = CreateSut(db);

        var gasto = NewLine(companyId, userId, typeId, amount: 10000m);
        var result = await sut.CreateAsync(gasto);

        var reloaded = await db.ExpenseReportLines.FirstAsync(g => g.Id == gasto.Id);
        Assert.Equal(10000m, reloaded.ReimbursementAmount);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task CreateAsync_AmountOverCap_ReimbursementCapped_WarningAdded()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_AmountOverCap_ReimbursementCapped_WarningAdded));
        var typeId = await CreateExpenseTypeAsync(db, companyId);
        var policies = new ExpensePolicyService(db, new PolicyProfileService(db));
        await policies.CreateAsync(companyId, typeId, null, null, ExpensePolicyPeriod.PerDocument, 15000m);
        var sut = CreateSut(db);

        var gasto = NewLine(companyId, userId, typeId, amount: 20000m);
        var result = await sut.CreateAsync(gasto);

        var reloaded = await db.ExpenseReportLines.FirstAsync(g => g.Id == gasto.Id);
        Assert.Equal(15000m, reloaded.ReimbursementAmount);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task CreateAsync_NoApplicablePolicy_ReimbursementEqualsAmount()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_NoApplicablePolicy_ReimbursementEqualsAmount));
        var typeId = await CreateExpenseTypeAsync(db, companyId);
        var sut = CreateSut(db);

        var gasto = NewLine(companyId, userId, typeId, amount: 999999m);
        var result = await sut.CreateAsync(gasto);

        var reloaded = await db.ExpenseReportLines.FirstAsync(g => g.Id == gasto.Id);
        Assert.Equal(999999m, reloaded.ReimbursementAmount);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task CreateAsync_QuantityMultiplier_CapIsMaxAmountTimesQuantity()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_QuantityMultiplier_CapIsMaxAmountTimesQuantity));
        var typeId = await CreateExpenseTypeAsync(db, companyId, "Movilización (Bip)", requiresQuantity: true);
        var policies = new ExpensePolicyService(db, new PolicyProfileService(db));
        await policies.CreateAsync(companyId, typeId, null, null, ExpensePolicyPeriod.PerDocument, 1700m);
        var sut = CreateSut(db);

        // Tope = 1700 * 3 = 5100. El monto pedido (6000) supera ese tope.
        var gasto = NewLine(companyId, userId, typeId, amount: 6000m, quantity: 3m);
        var result = await sut.CreateAsync(gasto);

        var reloaded = await db.ExpenseReportLines.FirstAsync(g => g.Id == gasto.Id);
        Assert.Equal(5100m, reloaded.ReimbursementAmount);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task CreateAsync_RequiresQuantityTrue_QuantityNull_TreatedAsOne()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_RequiresQuantityTrue_QuantityNull_TreatedAsOne));
        var typeId = await CreateExpenseTypeAsync(db, companyId, "Movilización (Bip)", requiresQuantity: true);
        var policies = new ExpensePolicyService(db, new PolicyProfileService(db));
        await policies.CreateAsync(companyId, typeId, null, null, ExpensePolicyPeriod.PerDocument, 1700m);
        var sut = CreateSut(db);

        // Quantity null -- ExpenseService debe tratarlo como 1 (tope = 1700 * 1 = 1700).
        var gasto = NewLine(companyId, userId, typeId, amount: 2000m, quantity: null);
        var result = await sut.CreateAsync(gasto);

        var reloaded = await db.ExpenseReportLines.FirstAsync(g => g.Id == gasto.Id);
        Assert.Equal(1700m, reloaded.ReimbursementAmount);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task CreateAsync_WeeklyPolicyExists_DoesNotAffectReimbursementAmount()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_WeeklyPolicyExists_DoesNotAffectReimbursementAmount));
        var typeId = await CreateExpenseTypeAsync(db, companyId);
        var policies = new ExpensePolicyService(db, new PolicyProfileService(db));
        // Solo existe una política Weekly con un tope bajo -- no debe tocar el cálculo de
        // ReimbursementAmount, que solo mira políticas PerDocument (Task 8 es la que usa Weekly).
        await policies.CreateAsync(companyId, typeId, null, null, ExpensePolicyPeriod.Weekly, 1000m);
        var sut = CreateSut(db);

        var gasto = NewLine(companyId, userId, typeId, amount: 10000m);
        var result = await sut.CreateAsync(gasto);

        var reloaded = await db.ExpenseReportLines.FirstAsync(g => g.Id == gasto.Id);
        Assert.Equal(10000m, reloaded.ReimbursementAmount);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task CreateAsync_NeverThrowsForAmountOverCap()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_NeverThrowsForAmountOverCap));
        var typeId = await CreateExpenseTypeAsync(db, companyId);
        var policies = new ExpensePolicyService(db, new PolicyProfileService(db));
        await policies.CreateAsync(companyId, typeId, null, null, ExpensePolicyPeriod.PerDocument, 1000m);
        var sut = CreateSut(db);

        var gasto = NewLine(companyId, userId, typeId, amount: 1000000m);

        // Antes de Task 7, un tope superado con IsBlocking=true lanzaba InvalidOperationException.
        // Ahora nunca lanza por monto -- el anti-objetivo de esta funcionalidad completa.
        var exception = await Record.ExceptionAsync(() => sut.CreateAsync(gasto));
        Assert.Null(exception);
    }

    [Fact]
    public async Task UpdateAsync_StillRequiresLooseStatus()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(UpdateAsync_StillRequiresLooseStatus));
        var typeId = await CreateExpenseTypeAsync(db, companyId);
        var sut = CreateSut(db);

        var gasto = NewLine(companyId, userId, typeId, amount: 1000m);
        await sut.CreateAsync(gasto);

        var tracked = await db.ExpenseReportLines.FirstAsync(g => g.Id == gasto.Id);
        tracked.Status = "InReport";
        await db.SaveChangesAsync();

        var cambios = NewLine(companyId, userId, typeId, amount: 2000m);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.UpdateAsync(gasto.Id, companyId, cambios));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public async Task UpdateAsync_ChangesCityIdAndQuantity_PersistsThem()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(UpdateAsync_ChangesCityIdAndQuantity_PersistsThem));
        var typeId = await CreateExpenseTypeAsync(db, companyId, "Movilización (Bip)", requiresQuantity: true);
        var city = new City { CompanyId = companyId, Name = "Santiago" };
        db.Cities.Add(city);
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var gasto = NewLine(companyId, userId, typeId, amount: 1000m, cityId: null, quantity: null);
        await sut.CreateAsync(gasto);

        var cambios = NewLine(companyId, userId, typeId, amount: 1000m, cityId: city.Id, quantity: 2m);
        await sut.UpdateAsync(gasto.Id, companyId, cambios);

        var reloaded = await db.ExpenseReportLines.FirstAsync(g => g.Id == gasto.Id);
        Assert.Equal(city.Id, reloaded.CityId);
        Assert.Equal(2m, reloaded.Quantity);
    }
}
