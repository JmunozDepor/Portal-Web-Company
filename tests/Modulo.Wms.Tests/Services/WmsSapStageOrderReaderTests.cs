using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageOrderReaderTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    [Fact]
    public async Task LeerPendientesAsync_ArmaCabeceraConLineasAnidadas()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdr = new WmsSapStageOrderHdr { CompanyId = companyId, OrderNbr = "VTA-1001", OrderType = "VTA", CardCode = "C001", CardName = "Cliente de prueba", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageOrderHdrs.Add(hdr);
        await contexto.SaveChangesAsync();
        contexto.WmsSapStageOrderDtls.Add(new WmsSapStageOrderDtl { ParentId = hdr.LineId, ItemCode = "ITM001", Quantity = 5m, LineNum = 0, SeqNbr = 1 });
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageOrderReader(contexto);
        var resultado = await reader.LeerPendientesAsync(companyId, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("Order", registro["TipoDocumento"]);
        Assert.Equal("VTA-1001", registro["OrderNbr"]);
        var lineas = (List<IntegrationRecord>)registro["Lineas"]!;
        var linea = Assert.Single(lineas);
        Assert.Equal("ITM001", linea["ItemCode"]);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Exito_ActualizaStatusYSyncedAt()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdr = new WmsSapStageOrderHdr { CompanyId = companyId, OrderNbr = "VTA-1001", OrderType = "VTA", CardCode = "C001", CardName = "Cliente de prueba", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageOrderHdrs.Add(hdr);
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageOrderReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: true, mensajeError: null, CancellationToken.None);

        var actualizada = await contexto.WmsSapStageOrderHdrs.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ProcesadoWms, actualizada.Status);
        Assert.NotNull(actualizada.SyncedAt);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Error_ActualizaStatusYErrorMsg()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdr = new WmsSapStageOrderHdr { CompanyId = companyId, OrderNbr = "VTA-1001", OrderType = "VTA", CardCode = "C001", CardName = "Cliente de prueba", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageOrderHdrs.Add(hdr);
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageOrderReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: false, mensajeError: "Rechazado por WMS", CancellationToken.None);

        var actualizada = await contexto.WmsSapStageOrderHdrs.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ErrorWms, actualizada.Status);
        Assert.Equal("Rechazado por WMS", actualizada.ErrorMsg);
    }
}
