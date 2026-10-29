using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

public class ExpenseTypeServiceTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    [Fact]
    public async Task CreateAsync_RequiresQuantityTrue_PersistsLabel()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_RequiresQuantityTrue_PersistsLabel));
        var sut = new ExpenseTypeService(db);

        var id = await sut.CreateAsync(
            companyId,
            "Movilización (Bip)",
            sapGlAccount: null,
            isMileage: false,
            ratePerKm: null,
            requiresQuantity: true,
            quantityLabel: "Días");

        var all = await sut.ListAllAsync(companyId);
        Assert.Single(all);
        Assert.Equal(id, all[0].Id);
        Assert.True(all[0].RequiresQuantity);
        Assert.Equal("Días", all[0].QuantityLabel);
    }

    [Fact]
    public async Task UpdateAsync_ChangesRequiresQuantityAndLabel()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(UpdateAsync_ChangesRequiresQuantityAndLabel));
        var sut = new ExpenseTypeService(db);
        var id = await sut.CreateAsync(
            companyId,
            "Combustible",
            sapGlAccount: null,
            isMileage: false,
            ratePerKm: null,
            requiresQuantity: false,
            quantityLabel: null);

        await sut.UpdateAsync(
            id,
            companyId,
            "Combustible",
            sapGlAccount: null,
            isActive: true,
            isMileage: false,
            ratePerKm: null,
            requiresQuantity: true,
            quantityLabel: "Litros");

        var all = await sut.ListAllAsync(companyId);
        Assert.Single(all);
        Assert.True(all[0].RequiresQuantity);
        Assert.Equal("Litros", all[0].QuantityLabel);
    }

    [Fact]
    public async Task CreateAsync_RequiresQuantityFalse_ClearsLabel()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_RequiresQuantityFalse_ClearsLabel));
        var sut = new ExpenseTypeService(db);

        var id = await sut.CreateAsync(
            companyId,
            "Peaje",
            sapGlAccount: null,
            isMileage: false,
            ratePerKm: null,
            requiresQuantity: false,
            quantityLabel: "Un rótulo que no debería guardarse");

        var all = await sut.ListAllAsync(companyId);
        Assert.Single(all);
        Assert.False(all[0].RequiresQuantity);
        Assert.Null(all[0].QuantityLabel);
    }
}
