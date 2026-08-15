using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSlshInventoryReaderTests
{
    private static WmsDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(options);
    }

    private static async Task<long> SembrarInboundStageAsync(WmsDbContext contexto, Guid companyId)
    {
        var stage = new WmsOracleInboundStage
        {
            CompanyId = companyId,
            TipoDoc = "SLSH",
            Formato = WmsInboundFormato.Xml,
            NombreArchivo = "test.xml",
            HashArchivo = Guid.NewGuid().ToString(),
            Contenido = "<xml/>",
            Estado = WmsInboundEstado.Aplanado,
        };
        contexto.WmsOracleInboundStages.Add(stage);
        await contexto.SaveChangesAsync();
        return stage.Id;
    }

    [Fact]
    public async Task LeerPendientesAsync_AgrupaLineasPorDocumentoBase()
    {
        await using var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var parentId = await SembrarInboundStageAsync(contexto, companyId);

        contexto.WmsOracleStageSlsh.Add(new WmsOracleStageSlsh
        {
            ParentId = parentId,
            Status = WmsSlshStatus.Pendiente,
            order_hdr_cust_field_4 = "42",
            order_dtl_cust_number_2 = "0",
            item_part_a = "ITEM-A",
            shipped_qty = "10",
        });
        contexto.WmsOracleStageSlsh.Add(new WmsOracleStageSlsh
        {
            ParentId = parentId,
            Status = WmsSlshStatus.Pendiente,
            order_hdr_cust_field_4 = "42",
            order_dtl_cust_number_2 = "1",
            item_part_a = "ITEM-B",
            shipped_qty = "5",
        });
        await contexto.SaveChangesAsync();

        var reader = new WmsSlshInventoryReader(contexto);
        var registros = await reader.LeerPendientesAsync(companyId, CancellationToken.None);

        Assert.Single(registros);
        var lineas = (List<IntegrationRecord>)registros[0]["Lineas"]!;
        Assert.Equal(2, lineas.Count);
        Assert.Equal("ITEM-A", lineas[0]["ItemCode"]);
        Assert.Equal(42, lineas[0]["BaseEntry"]);
    }

    [Fact]
    public async Task LeerPendientesAsync_SoloTraeFilasDeLaCompaniaPedida()
    {
        await using var contexto = CrearContexto();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var parentA = await SembrarInboundStageAsync(contexto, companyA);
        var parentB = await SembrarInboundStageAsync(contexto, companyB);

        contexto.WmsOracleStageSlsh.Add(new WmsOracleStageSlsh { ParentId = parentA, Status = WmsSlshStatus.Pendiente, order_hdr_cust_field_4 = "1", item_part_a = "A" });
        contexto.WmsOracleStageSlsh.Add(new WmsOracleStageSlsh { ParentId = parentB, Status = WmsSlshStatus.Pendiente, order_hdr_cust_field_4 = "2", item_part_a = "B" });
        await contexto.SaveChangesAsync();

        var reader = new WmsSlshInventoryReader(contexto);
        var registros = await reader.LeerPendientesAsync(companyA, CancellationToken.None);

        Assert.Single(registros);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_ConExito_ActualizaStatusAProcesadoSap()
    {
        await using var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var parentId = await SembrarInboundStageAsync(contexto, companyId);
        var fila = new WmsOracleStageSlsh { ParentId = parentId, Status = WmsSlshStatus.Pendiente, order_hdr_cust_field_4 = "42", item_part_a = "A" };
        contexto.WmsOracleStageSlsh.Add(fila);
        await contexto.SaveChangesAsync();

        var reader = new WmsSlshInventoryReader(contexto);
        var registros = await reader.LeerPendientesAsync(companyId, CancellationToken.None);
        await reader.MarcarProcesadoAsync(companyId, registros[0], exito: true, mensajeError: null, CancellationToken.None);

        var filaActualizada = await contexto.WmsOracleStageSlsh.FirstAsync(f => f.LineId == fila.LineId);
        Assert.Equal(WmsSlshStatus.ProcesadoSap, filaActualizada.Status);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_ConError_ActualizaStatusAErrorSapConMensaje()
    {
        await using var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var parentId = await SembrarInboundStageAsync(contexto, companyId);
        var fila = new WmsOracleStageSlsh { ParentId = parentId, Status = WmsSlshStatus.Pendiente, order_hdr_cust_field_4 = "42", item_part_a = "A" };
        contexto.WmsOracleStageSlsh.Add(fila);
        await contexto.SaveChangesAsync();

        var reader = new WmsSlshInventoryReader(contexto);
        var registros = await reader.LeerPendientesAsync(companyId, CancellationToken.None);
        await reader.MarcarProcesadoAsync(companyId, registros[0], exito: false, mensajeError: "SAP rechazó el documento", CancellationToken.None);

        var filaActualizada = await contexto.WmsOracleStageSlsh.FirstAsync(f => f.LineId == fila.LineId);
        Assert.Equal(WmsSlshStatus.ErrorSap, filaActualizada.Status);
        Assert.Equal("SAP rechazó el documento", filaActualizada.ErrorMsg);
    }
}
