using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

public class CityServiceTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    [Fact]
    public async Task CreateAsync_PersistsCity_AndListActiveAsync_ReturnsIt()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_PersistsCity_AndListActiveAsync_ReturnsIt));
        var sut = new CityService(db);

        var id = await sut.CreateAsync(companyId, "Santiago");

        var active = await sut.ListActiveAsync(companyId);
        Assert.Single(active);
        Assert.Equal(id, active[0].Id);
        Assert.Equal("Santiago", active[0].Name);
        Assert.True(active[0].IsActive);
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameSameCompany_Throws()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CreateAsync_DuplicateNameSameCompany_Throws));
        var sut = new CityService(db);

        await sut.CreateAsync(companyId, "Concepción");

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateAsync(companyId, "Concepción"));
    }

    [Fact]
    public async Task UpdateAsync_ChangesNameAndIsActive()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(UpdateAsync_ChangesNameAndIsActive));
        var sut = new CityService(db);
        var id = await sut.CreateAsync(companyId, "Valparaíso");

        await sut.UpdateAsync(id, companyId, "Viña del Mar", isActive: false);

        var all = await sut.ListAllAsync(companyId);
        Assert.Single(all);
        Assert.Equal("Viña del Mar", all[0].Name);
        Assert.False(all[0].IsActive);
    }

    [Fact]
    public async Task ListActiveAsync_ExcludesInactive()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(ListActiveAsync_ExcludesInactive));
        var sut = new CityService(db);
        var activeId = await sut.CreateAsync(companyId, "Antofagasta");
        var inactiveId = await sut.CreateAsync(companyId, "Iquique");
        await sut.UpdateAsync(inactiveId, companyId, "Iquique", isActive: false);

        var active = await sut.ListActiveAsync(companyId);

        Assert.Single(active);
        Assert.Equal(activeId, active[0].Id);
    }
}
