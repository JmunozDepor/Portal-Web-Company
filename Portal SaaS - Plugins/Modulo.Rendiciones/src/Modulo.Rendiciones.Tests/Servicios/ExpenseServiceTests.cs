using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using Modulo.Rendiciones.Tests.Fakes;

namespace Modulo.Rendiciones.Tests.Servicios;

/// <summary>
/// ExpenseReportLine ganó tres columnas nuevas (CityId, Quantity, ReimbursementAmount) para
/// soportar el motor de políticas por ciudad/perfil (Task 7). Este task es puramente de
/// esquema: nada acá calcula el reembolso real todavía -- solo se verifica que el código
/// existente (CreateAsync), sin tocar los campos nuevos, deja ReimbursementAmount == Amount
/// y CityId/Quantity en null.
/// </summary>
public class ExpenseServiceTests
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

    private sealed class NoopPolicyService : IExpensePolicyService
    {
        public Task<IReadOnlyList<ExpensePolicy>> ListAsync(Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CreateAsync(Guid companyId, long expenseTypeId, decimal? maxAmount, bool isBlocking, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(long id, Guid companyId, decimal? maxAmount, bool isBlocking, bool isActive, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<ExpenseValidationResult> ValidateAsync(Guid companyId, Guid userId, long? expenseTypeId, decimal amount,
            string? documentNumber, string? supplierTaxId, long? expenseIdToExclude, CancellationToken ct = default) =>
            Task.FromResult(new ExpenseValidationResult(false, Array.Empty<string>()));
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

    private static ExpenseService CreateSut(RendicionesDbContext db) =>
        new(db, new NoopAttachmentService(), new NoopPolicyService(), new NoopRoutingService(), new NoopSupplierHintService(),
            NullLogger<ExpenseService>.Instance);

    [Fact]
    public async Task CreateAsync_NewLine_ReimbursementAmountDefaultsToAmount()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var db = CreateDb(nameof(CreateAsync_NewLine_ReimbursementAmountDefaultsToAmount));
        var sut = CreateSut(db);

        var gasto = new ExpenseReportLine
        {
            CompanyId = companyId,
            UserId = userId,
            Amount = 12345,
            Currency = "CLP",
            Date = DateTimeOffset.UtcNow,
        };

        await sut.CreateAsync(gasto);

        var reloaded = await db.ExpenseReportLines.FirstAsync(g => g.Id == gasto.Id);
        Assert.Equal(reloaded.Amount, reloaded.ReimbursementAmount);
        Assert.Null(reloaded.CityId);
        Assert.Null(reloaded.Quantity);
    }

    [Fact]
    public async Task UpdateAsync_ChangedAmount_ReimbursementAmountTracksNewAmount()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var db = CreateDb(nameof(UpdateAsync_ChangedAmount_ReimbursementAmountTracksNewAmount));
        var sut = CreateSut(db);

        var gasto = new ExpenseReportLine
        {
            CompanyId = companyId,
            UserId = userId,
            Amount = 1000,
            Currency = "CLP",
            Date = DateTimeOffset.UtcNow,
        };
        await sut.CreateAsync(gasto);

        var cambios = new ExpenseReportLine
        {
            CompanyId = companyId,
            UserId = userId,
            Amount = 5000,
            Currency = "CLP",
            Date = DateTimeOffset.UtcNow,
        };
        await sut.UpdateAsync(gasto.Id, companyId, cambios);

        var reloaded = await db.ExpenseReportLines.FirstAsync(g => g.Id == gasto.Id);
        Assert.Equal(5000, reloaded.Amount);
        Assert.Equal(5000, reloaded.ReimbursementAmount);
    }
}
