using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSvshInventoryReaderTests
{
    private static WmsDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(options);
    }

    private static async Task<long> SembrarInboundStageAsync(WmsDbContext contexto, Guid companyId, string hash)
    {
        var stage = new WmsOracleInboundStage
        {
            CompanyId = companyId,
            TipoDoc = "SVSH",
            Formato = WmsInboundFormato.Xml,
            NombreArchivo = "test.xml",
            HashArchivo = hash,
            Contenido = "",
        };
        contexto.WmsOracleInboundStages.Add(stage);
        await contexto.SaveChangesAsync();
        return stage.Id;
    }

    [Fact]
    public async Task LeerPendientesAsync_AgrupaPorShipmentYBaseEntry_ArmaUnRegistroPorGrupo()
    {
        await using var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var parentId = await SembrarInboundStageAsync(contexto, companyId, "h1");

        contexto.WmsOracleStageSvsh.AddRange(
            new WmsOracleStageSvsh { ParentId = parentId, shipment_nbr = "ASN1", shipment_dtl_cust_field_1 = "1250000001", shipment_dtl_cust_field_2 = "700", item_part_a = "ITEM-A", received_qty = "4", Status = WmsSvshStatus.Pendiente },
            new WmsOracleStageSvsh { ParentId = parentId, shipment_nbr = "ASN1", shipment_dtl_cust_field_1 = "1250000001", shipment_dtl_cust_field_2 = "700", item_part_a = "ITEM-B", received_qty = "2", Status = WmsSvshStatus.Pendiente },
            new WmsOracleStageSvsh { ParentId = parentId, shipment_nbr = "ASN2", shipment_dtl_cust_field_1 = "1250000001", shipment_dtl_cust_field_2 = "701", item_part_a = "ITEM-C", received_qty = "1", Status = WmsSvshStatus.ProcesadoSap });
        await contexto.SaveChangesAsync();

        var reader = new WmsSvshInventoryReader(contexto);
        var registros = await reader.LeerPendientesAsync(companyId, null, CancellationToken.None);

        Assert.Single(registros);
        Assert.Equal("Inventory", registros[0]["TipoDocumento"]);
        var lineas = (List<IntegrationRecord>)registros[0]["Lineas"]!;
        Assert.Equal(2, lineas.Count);
    }

    [Fact]
    public async Task LeerPendientesAsync_BaseTypeDistintoDeStockTransfer_ArmaTipoDocumentoPurchase()
    {
        await using var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var parentId = await SembrarInboundStageAsync(contexto, companyId, "h-purchase");

        contexto.WmsOracleStageSvsh.Add(new WmsOracleStageSvsh
        {
            ParentId = parentId,
            shipment_nbr = "ASN9",
            shipment_dtl_cust_field_1 = "20",
            shipment_dtl_cust_field_2 = "900",
            item_part_a = "ITEM-X",
            received_qty = "1",
            Status = WmsSvshStatus.Pendiente,
        });
        await contexto.SaveChangesAsync();

        var reader = new WmsSvshInventoryReader(contexto);
        var registros = await reader.LeerPendientesAsync(companyId, null, CancellationToken.None);

        Assert.Single(registros);
        Assert.Equal("Purchase", registros[0]["TipoDocumento"]);
    }

    [Fact]
    public async Task LeerPendientesAsync_SoloTraeFilasDeLaCompaniaPedida()
    {
        await using var contexto = CrearContexto();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var parentA = await SembrarInboundStageAsync(contexto, companyA, "hA");
        var parentB = await SembrarInboundStageAsync(contexto, companyB, "hB");

        contexto.WmsOracleStageSvsh.Add(new WmsOracleStageSvsh { ParentId = parentA, shipment_nbr = "A1", shipment_dtl_cust_field_1 = "1250000001", shipment_dtl_cust_field_2 = "1", item_part_a = "A", Status = WmsSvshStatus.Pendiente });
        contexto.WmsOracleStageSvsh.Add(new WmsOracleStageSvsh { ParentId = parentB, shipment_nbr = "B1", shipment_dtl_cust_field_1 = "1250000001", shipment_dtl_cust_field_2 = "2", item_part_a = "B", Status = WmsSvshStatus.Pendiente });
        await contexto.SaveChangesAsync();

        var reader = new WmsSvshInventoryReader(contexto);
        var registros = await reader.LeerPendientesAsync(companyA, null, CancellationToken.None);

        Assert.Single(registros);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Exito_MarcaLasLineasComoProcesadoSap()
    {
        await using var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var parentId = await SembrarInboundStageAsync(contexto, companyId, "h2");

        var fila = new WmsOracleStageSvsh { ParentId = parentId, shipment_nbr = "ASN3", shipment_dtl_cust_field_1 = "1250000001", shipment_dtl_cust_field_2 = "800", item_part_a = "ITEM-D", received_qty = "1", Status = WmsSvshStatus.Pendiente };
        contexto.WmsOracleStageSvsh.Add(fila);
        await contexto.SaveChangesAsync();

        var reader = new WmsSvshInventoryReader(contexto);
        var registro = new IntegrationRecord(
            new Dictionary<string, object?> { ["_StagingLineIds"] = new List<long> { fila.LineId } });

        await reader.MarcarProcesadoAsync(companyId, registro, exito: true, mensajeError: null, CancellationToken.None);

        var actualizada = await contexto.WmsOracleStageSvsh.SingleAsync(f => f.LineId == fila.LineId);
        Assert.Equal(WmsSvshStatus.ProcesadoSap, actualizada.Status);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_ConError_ActualizaStatusAErrorSapConMensaje()
    {
        await using var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var parentId = await SembrarInboundStageAsync(contexto, companyId, "h3");

        var fila = new WmsOracleStageSvsh { ParentId = parentId, shipment_nbr = "ASN4", shipment_dtl_cust_field_1 = "1250000001", shipment_dtl_cust_field_2 = "801", item_part_a = "ITEM-E", received_qty = "1", Status = WmsSvshStatus.Pendiente };
        contexto.WmsOracleStageSvsh.Add(fila);
        await contexto.SaveChangesAsync();

        var reader = new WmsSvshInventoryReader(contexto);
        var registro = new IntegrationRecord(
            new Dictionary<string, object?> { ["_StagingLineIds"] = new List<long> { fila.LineId } });

        await reader.MarcarProcesadoAsync(companyId, registro, exito: false, mensajeError: "SAP rechazó el documento", CancellationToken.None);

        var actualizada = await contexto.WmsOracleStageSvsh.SingleAsync(f => f.LineId == fila.LineId);
        Assert.Equal(WmsSvshStatus.ErrorSap, actualizada.Status);
        Assert.Equal("SAP rechazó el documento", actualizada.ErrorMsg);
    }
}
