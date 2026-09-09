using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class ValidationFieldServiceTests
{
    private static WmsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task CreateAsync_CampoNuevo_QuedaNormalizadoEnMinuscula()
    {
        var ctx = CrearContexto();
        var svc = new ValidationFieldService(ctx);
        var company = Guid.NewGuid();

        await svc.CreateAsync(company, "Store", "  City  ", isActive: true);

        var fila = Assert.Single(ctx.ValidationFields);
        Assert.Equal("Store", fila.TipoEntidad);
        Assert.Equal("city", fila.FieldName);
        Assert.True(fila.IsActive);
    }

    [Fact]
    public async Task CreateAsync_TipoEntidadNoSoportado_Rechaza()
    {
        var svc = new ValidationFieldService(CrearContexto());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync(Guid.NewGuid(), "Pedido", "algo", true));
    }

    [Fact]
    public async Task CreateAsync_Duplicado_Rechaza()
    {
        var ctx = CrearContexto();
        var svc = new ValidationFieldService(ctx);
        var company = Guid.NewGuid();

        await svc.CreateAsync(company, "Item", "brand_code", true);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync(company, "Item", "BRAND_CODE", true));
    }

    [Fact]
    public async Task SetActiveAsync_CambiaElFlag()
    {
        var ctx = CrearContexto();
        var svc = new ValidationFieldService(ctx);
        var company = Guid.NewGuid();
        var id = await svc.CreateAsync(company, "Store", "zip", true);

        await svc.SetActiveAsync(id, company, false);

        Assert.False(ctx.ValidationFields.Single().IsActive);
    }

    [Fact]
    public async Task DeleteAsync_DeOtraCompania_Rechaza()
    {
        var ctx = CrearContexto();
        var svc = new ValidationFieldService(ctx);
        var id = await svc.CreateAsync(Guid.NewGuid(), "Store", "name", true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DeleteAsync(id, Guid.NewGuid()));
    }
}
