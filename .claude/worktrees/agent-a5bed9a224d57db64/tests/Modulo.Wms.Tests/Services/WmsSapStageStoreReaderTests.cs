using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageStoreReaderTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    [Fact]
    public async Task LeerPendientesAsync_SoloTraePendientesDeLaCompania()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var otraCompanyId = Guid.NewGuid();

        contexto.WmsSapStageStores.AddRange(
            new WmsSapStageStore { CompanyId = companyId, CardCode = "C001", CardName = "A", Status = WmsSapStageStatus.Pendiente },
            new WmsSapStageStore { CompanyId = companyId, CardCode = "C002", CardName = "B", Status = WmsSapStageStatus.ProcesadoWms },
            new WmsSapStageStore { CompanyId = otraCompanyId, CardCode = "C003", CardName = "C", Status = WmsSapStageStatus.Pendiente });
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageStoreReader(contexto);
        var resultado = await reader.LeerPendientesAsync(companyId, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("Store", registro["TipoDocumento"]);
        Assert.Equal("C001", registro["CardCode"]);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Exito_ActualizaStatusYSyncedAt()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var fila = new WmsSapStageStore { CompanyId = companyId, CardCode = "C001", CardName = "A", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageStores.Add(fila);
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageStoreReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: true, mensajeError: null, CancellationToken.None);

        var actualizada = await contexto.WmsSapStageStores.SingleAsync();
        Assert.Equal(WmsSapStageStatus.Enviado, actualizada.Status);
        Assert.NotNull(actualizada.SyncedAt);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Error_ActualizaStatusYErrorMsg()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var fila = new WmsSapStageStore { CompanyId = companyId, CardCode = "C001", CardName = "A", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageStores.Add(fila);
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageStoreReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: false, mensajeError: "Rechazado por WMS", CancellationToken.None);

        var actualizada = await contexto.WmsSapStageStores.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ErrorWms, actualizada.Status);
        Assert.Equal("Rechazado por WMS", actualizada.ErrorMsg);
    }
}
