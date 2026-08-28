using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsConfirmacionServiceTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    [Fact]
    public async Task BuscarAsync_TipoIngreso_AgrupaLineasPorShipmentNbr()
    {
        var companyId = Guid.NewGuid();
        var contexto = CrearContexto();

        var stage = new WmsOracleInboundStage { CompanyId = companyId, TipoDoc = "SVSH", Formato = WmsInboundFormato.Xml, NombreArchivo = "a.xml", HashArchivo = "hh1", Contenido = "" };
        contexto.WmsOracleInboundStages.Add(stage);
        await contexto.SaveChangesAsync();

        contexto.WmsOracleStageSvsh.AddRange(
            new WmsOracleStageSvsh { ParentId = stage.Id, shipment_nbr = "ASN9", item_part_a = "X", Status = WmsSvshStatus.ErrorSap, ErrorMsg = "falló" },
            new WmsOracleStageSvsh { ParentId = stage.Id, shipment_nbr = "ASN9", item_part_a = "Y", Status = WmsSvshStatus.ErrorSap, ErrorMsg = "falló" });
        await contexto.SaveChangesAsync();

        var service = new WmsConfirmacionService(contexto);
        var resultado = await service.BuscarAsync(companyId, new WmsConfirmacionFiltro { Tipo = WmsTipoTransaccion.ConfirmacionIngreso }, CancellationToken.None);

        Assert.Single(resultado.Items);
        Assert.Equal("ASN9", resultado.Items[0].Documento);
        Assert.Equal(2, resultado.Items[0].LineasCount);
    }

    [Fact]
    public async Task BuscarAsync_TipoOrdenes_AgrupaLineasPorOrderHdrCustField4()
    {
        var companyId = Guid.NewGuid();
        var contexto = CrearContexto();

        var stage = new WmsOracleInboundStage { CompanyId = companyId, TipoDoc = "SLSH", Formato = WmsInboundFormato.Xml, NombreArchivo = "b.xml", HashArchivo = "hh2", Contenido = "" };
        contexto.WmsOracleInboundStages.Add(stage);
        await contexto.SaveChangesAsync();

        contexto.WmsOracleStageSlsh.AddRange(
            new WmsOracleStageSlsh { ParentId = stage.Id, order_hdr_cust_field_4 = "ORD1", Status = WmsSlshStatus.ProcesadoSap },
            new WmsOracleStageSlsh { ParentId = stage.Id, order_hdr_cust_field_4 = "ORD1", Status = WmsSlshStatus.ProcesadoSap },
            new WmsOracleStageSlsh { ParentId = stage.Id, order_hdr_cust_field_4 = "ORD2", Status = WmsSlshStatus.Pendiente });
        await contexto.SaveChangesAsync();

        var service = new WmsConfirmacionService(contexto);
        var resultado = await service.BuscarAsync(companyId, new WmsConfirmacionFiltro { Tipo = WmsTipoTransaccion.ConfirmacionOrdenes }, CancellationToken.None);

        Assert.Equal(2, resultado.Items.Count);
        var ord1 = resultado.Items.Single(r => r.Documento == "ORD1");
        Assert.Equal(2, ord1.LineasCount);
    }

    [Fact]
    public async Task BuscarAsync_ExcluyeFilasDeOtraCompania()
    {
        var companyId = Guid.NewGuid();
        var otraCompanyId = Guid.NewGuid();
        var contexto = CrearContexto();

        var stage = new WmsOracleInboundStage { CompanyId = companyId, TipoDoc = "SVSH", Formato = WmsInboundFormato.Xml, NombreArchivo = "a.xml", HashArchivo = "hh1", Contenido = "" };
        var otroStage = new WmsOracleInboundStage { CompanyId = otraCompanyId, TipoDoc = "SVSH", Formato = WmsInboundFormato.Xml, NombreArchivo = "z.xml", HashArchivo = "hhz", Contenido = "" };
        contexto.WmsOracleInboundStages.AddRange(stage, otroStage);
        await contexto.SaveChangesAsync();

        contexto.WmsOracleStageSvsh.AddRange(
            new WmsOracleStageSvsh { ParentId = stage.Id, shipment_nbr = "ASN9", item_part_a = "X", Status = WmsSvshStatus.ErrorSap, ErrorMsg = "falló" },
            new WmsOracleStageSvsh { ParentId = otroStage.Id, shipment_nbr = "ASN-OTRA", item_part_a = "Y", Status = WmsSvshStatus.ErrorSap, ErrorMsg = "falló" });
        await contexto.SaveChangesAsync();

        var service = new WmsConfirmacionService(contexto);
        var resultado = await service.BuscarAsync(companyId, new WmsConfirmacionFiltro { Tipo = WmsTipoTransaccion.ConfirmacionIngreso }, CancellationToken.None);

        Assert.Single(resultado.Items);
        Assert.Equal("ASN9", resultado.Items[0].Documento);
    }

    [Fact]
    public async Task ResetearAsync_VuelveElDocumentoAPendienteYLimpiaError()
    {
        var companyId = Guid.NewGuid();
        var contexto = CrearContexto();

        var stage = new WmsOracleInboundStage { CompanyId = companyId, TipoDoc = "SVSH", Formato = WmsInboundFormato.Xml, NombreArchivo = "c.xml", HashArchivo = "hh3", Contenido = "" };
        contexto.WmsOracleInboundStages.Add(stage);
        await contexto.SaveChangesAsync();

        contexto.WmsOracleStageSvsh.Add(
            new WmsOracleStageSvsh { ParentId = stage.Id, shipment_nbr = "ASN9", Status = WmsSvshStatus.ErrorSap, ErrorMsg = "falló" });
        await contexto.SaveChangesAsync();

        var service = new WmsConfirmacionService(contexto);
        await service.ResetearAsync(companyId, WmsTipoTransaccion.ConfirmacionIngreso, "ASN9", CancellationToken.None);

        var actualizado = await contexto.WmsOracleStageSvsh.SingleAsync();
        Assert.Equal(WmsSvshStatus.Pendiente, actualizado.Status);
        Assert.Null(actualizado.ErrorMsg);
    }
}
