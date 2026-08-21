using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsTransaccionServiceTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    [Fact]
    public async Task BuscarAsync_FiltraPorEstadoYTipo()
    {
        var companyId = Guid.NewGuid();
        var contexto = CrearContexto();

        contexto.WmsSapStageItems.AddRange(
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "I1", ItemName = "Item 1", Status = WmsSapStageStatus.ErrorWms },
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "I2", ItemName = "Item 2", Status = WmsSapStageStatus.ProcesadoWms });
        await contexto.SaveChangesAsync();

        var service = new WmsTransaccionService(contexto);
        var resultado = await service.BuscarAsync(companyId, new WmsTransaccionFiltro { Tipo = WmsTipoTransaccion.EnvioProducto, Estado = "ErrorWms" }, CancellationToken.None);

        Assert.Single(resultado.Items);
        Assert.Equal("I1", resultado.Items[0].Documento);
    }

    [Fact]
    public async Task ResetearAsync_VuelveElEstadoAPendienteYLimpiaError()
    {
        var companyId = Guid.NewGuid();
        var contexto = CrearContexto();

        var item = new WmsSapStageItem { CompanyId = companyId, ItemCode = "I1", ItemName = "Item 1", Status = WmsSapStageStatus.ErrorWms, ErrorMsg = "boom" };
        contexto.WmsSapStageItems.Add(item);
        await contexto.SaveChangesAsync();

        var service = new WmsTransaccionService(contexto);
        await service.ResetearAsync(companyId, WmsTipoTransaccion.EnvioProducto, new List<long> { item.LineId }, CancellationToken.None);

        var actualizado = await contexto.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.Pendiente, actualizado.Status);
        Assert.Null(actualizado.ErrorMsg);
    }
}
