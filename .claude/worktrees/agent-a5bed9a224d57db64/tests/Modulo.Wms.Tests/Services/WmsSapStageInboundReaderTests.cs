using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageInboundReaderTests
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
        var hdr = new WmsSapStageInboundHdr { CompanyId = companyId, SapDocEntry = 500123, ShipmentType = "TRASLADO_ESTANDAR", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageInboundHdrs.Add(hdr);
        await contexto.SaveChangesAsync();
        contexto.WmsSapStageInboundDtls.Add(new WmsSapStageInboundDtl { ParentId = hdr.LineId, ItemCode = "ITM001", Quantity = 10m, WhsCode = "01", LineNum = 0 });
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageInboundReader(contexto);
        var resultado = await reader.LeerPendientesAsync(companyId, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("IbShipment", registro["TipoDocumento"]);
        Assert.Equal(500123, registro["SapDocEntry"]);
        var lineas = (List<IntegrationRecord>)registro["Lineas"]!;
        var linea = Assert.Single(lineas);
        Assert.Equal("ITM001", linea["ItemCode"]);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Error_ActualizaSoloLaCabecera()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdr = new WmsSapStageInboundHdr { CompanyId = companyId, SapDocEntry = 500123, ShipmentType = "TRASLADO_ESTANDAR", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageInboundHdrs.Add(hdr);
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageInboundReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: false, mensajeError: "BaseEntry inválido", CancellationToken.None);

        var actualizada = await contexto.WmsSapStageInboundHdrs.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ErrorWms, actualizada.Status);
        Assert.Equal("BaseEntry inválido", actualizada.ErrorMsg);
    }
}
