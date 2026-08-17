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
    public async Task LeerPendientesAsync_IncluyeCamposQueAntesSePerdianEnLaFronteraTask2ATask4()
    {
        // Fix 2 de la ronda de correcciones de revisión final: PickListAbsEntry,
        // BaseObjectType, BaseEntry, CardCode y CardName ya se escribían en la tabla de
        // staging (Task 2) pero LeerPendientesAsync (Task 4) nunca los incluía en el
        // IntegrationRecord, así que nunca llegaban a WmsCloudConnector (Task 5).
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdr = new WmsSapStageOrderHdr
        {
            CompanyId = companyId,
            OrderNbr = "VTA-1001",
            OrderType = "VTA",
            CardCode = "C001",
            CardName = "Cliente de prueba",
            PickListAbsEntry = 100,
            BaseObjectType = 17,
            BaseEntry = 500,
            Status = WmsSapStageStatus.Pendiente,
        };
        contexto.WmsSapStageOrderHdrs.Add(hdr);
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageOrderReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, CancellationToken.None)).Single();

        Assert.Equal(100, registro["PickListAbsEntry"]);
        Assert.Equal(17, registro["BaseObjectType"]);
        Assert.Equal(500, registro["BaseEntry"]);
        Assert.Equal("C001", registro["CardCode"]);
        Assert.Equal("Cliente de prueba", registro["CardName"]);
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
        Assert.Equal(WmsSapStageStatus.Enviado, actualizada.Status);
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
