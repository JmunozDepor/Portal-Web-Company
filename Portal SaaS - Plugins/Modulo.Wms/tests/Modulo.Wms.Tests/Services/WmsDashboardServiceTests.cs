using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsDashboardServiceTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    [Fact]
    public async Task ObtenerResumenAsync_CuentaPorEstadoYTipo()
    {
        var companyId = Guid.NewGuid();
        var contexto = CrearContexto();

        contexto.WmsSapStageItems.AddRange(
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "I1", ItemName = "Item 1", Status = WmsSapStageStatus.ProcesadoWms },
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "I2", ItemName = "Item 2", Status = WmsSapStageStatus.ErrorWms },
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "I3", ItemName = "Item 3", Status = WmsSapStageStatus.Pendiente });
        await contexto.SaveChangesAsync();

        var service = new WmsDashboardService(contexto);
        var resumen = await service.ObtenerResumenAsync(companyId, DateTime.UtcNow.AddDays(-1), CancellationToken.None);

        var producto = resumen.PorTipo.Single(t => t.Tipo == WmsTipoTransaccion.EnvioProducto);
        Assert.Equal(1, producto.Ok);
        Assert.Equal(1, producto.Error);
        Assert.Equal(1, producto.Pendiente);
        Assert.Equal(1, resumen.TotalOk);
    }

    [Fact]
    public async Task ObtenerResumenAsync_ExcluyeFilasDeOtraCompania()
    {
        var companyId = Guid.NewGuid();
        var otraCompanyId = Guid.NewGuid();
        var contexto = CrearContexto();

        contexto.WmsSapStageItems.AddRange(
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "I1", ItemName = "Item 1", Status = WmsSapStageStatus.ProcesadoWms },
            new WmsSapStageItem { CompanyId = otraCompanyId, ItemCode = "I2", ItemName = "Item 2", Status = WmsSapStageStatus.ProcesadoWms });
        await contexto.SaveChangesAsync();

        var service = new WmsDashboardService(contexto);
        var resumen = await service.ObtenerResumenAsync(companyId, DateTime.UtcNow.AddDays(-1), CancellationToken.None);

        var producto = resumen.PorTipo.Single(t => t.Tipo == WmsTipoTransaccion.EnvioProducto);
        Assert.Equal(1, producto.Ok);
        Assert.Equal(1, resumen.TotalOk);
    }
}
