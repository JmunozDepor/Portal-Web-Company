using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageInboundWriterTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    private static IntegrationRecord CrearRegistroTraslado(int docEntry, DateTime sourceUpdateDate) =>
        new(new Dictionary<string, object?>
        {
            ["SapDocEntry"] = docEntry,
            ["ShipmentType"] = "TRASLADO_ESTANDAR",
            ["SourceUpdateDate"] = sourceUpdateDate,
            ["Lineas"] = new List<IntegrationRecord>
            {
                new(new Dictionary<string, object?>
                {
                    ["ItemCode"] = "ITM001",
                    ["Quantity"] = 10m,
                    ["WhsCode"] = "01",
                    ["LineNum"] = 0,
                }),
            },
        });

    [Fact]
    public async Task EscribirAsync_TrasladoNuevo_InsertaCabeceraYDetalle()
    {
        var contexto = CrearContexto();
        var writer = new WmsSapStageInboundWriter(contexto);
        var companyId = Guid.NewGuid();

        await writer.EscribirAsync(companyId, [CrearRegistroTraslado(500123, new DateTime(2026, 8, 15))], CancellationToken.None);

        var hdr = Assert.Single(contexto.WmsSapStageInboundHdrs);
        Assert.Equal(500123, hdr.SapDocEntry);
        Assert.Equal(WmsSapStageStatus.Pendiente, hdr.Status);

        var dtl = Assert.Single(contexto.WmsSapStageInboundDtls);
        Assert.Equal(hdr.LineId, dtl.ParentId);
        Assert.Equal("ITM001", dtl.ItemCode);
        Assert.Equal(10m, dtl.Quantity);
    }

    [Fact]
    public async Task EscribirAsync_TrasladoYaProcesadoConCambioEnSap_VuelveAPendienteYReemplazaDetalle()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdrExistente = new WmsSapStageInboundHdr
        {
            CompanyId = companyId,
            SapDocEntry = 500123,
            ShipmentType = "TRASLADO_ESTANDAR",
            SourceUpdateDate = new DateTime(2026, 8, 10),
            Status = WmsSapStageStatus.ProcesadoWms,
        };
        contexto.WmsSapStageInboundHdrs.Add(hdrExistente);
        await contexto.SaveChangesAsync();
        contexto.WmsSapStageInboundDtls.Add(new WmsSapStageInboundDtl
        {
            ParentId = hdrExistente.LineId,
            ItemCode = "ITM_VIEJO",
            Quantity = 1m,
            WhsCode = "01",
            LineNum = 0,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageInboundWriter(contexto);
        await writer.EscribirAsync(companyId, [CrearRegistroTraslado(500123, new DateTime(2026, 8, 15))], CancellationToken.None);

        var hdr = Assert.Single(contexto.WmsSapStageInboundHdrs);
        Assert.Equal(WmsSapStageStatus.Pendiente, hdr.Status);

        var dtl = Assert.Single(contexto.WmsSapStageInboundDtls);
        Assert.Equal("ITM001", dtl.ItemCode);
    }

    [Fact]
    public async Task EscribirAsync_TrasladoYaPendienteSinCambios_NoDuplicaDetalle()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdrExistente = new WmsSapStageInboundHdr
        {
            CompanyId = companyId,
            SapDocEntry = 500123,
            ShipmentType = "TRASLADO_ESTANDAR",
            SourceUpdateDate = new DateTime(2026, 8, 15),
            Status = WmsSapStageStatus.Pendiente,
        };
        contexto.WmsSapStageInboundHdrs.Add(hdrExistente);
        await contexto.SaveChangesAsync();
        contexto.WmsSapStageInboundDtls.Add(new WmsSapStageInboundDtl
        {
            ParentId = hdrExistente.LineId,
            ItemCode = "ITM001",
            Quantity = 10m,
            WhsCode = "01",
            LineNum = 0,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageInboundWriter(contexto);
        await writer.EscribirAsync(companyId, [CrearRegistroTraslado(500123, new DateTime(2026, 8, 15))], CancellationToken.None);

        Assert.Single(contexto.WmsSapStageInboundHdrs);
        Assert.Single(contexto.WmsSapStageInboundDtls);
    }

    [Fact]
    public async Task EscribirAsync_TrasladoEnErrorWms_VuelveAPendienteYReemplazaDetalleSinDuplicar()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdrExistente = new WmsSapStageInboundHdr
        {
            CompanyId = companyId,
            SapDocEntry = 500123,
            ShipmentType = "TRASLADO_ESTANDAR",
            SourceUpdateDate = new DateTime(2026, 8, 10),
            Status = WmsSapStageStatus.ErrorWms,
            ErrorMsg = "Error al postear en WMS",
        };
        contexto.WmsSapStageInboundHdrs.Add(hdrExistente);
        await contexto.SaveChangesAsync();
        contexto.WmsSapStageInboundDtls.Add(new WmsSapStageInboundDtl
        {
            ParentId = hdrExistente.LineId,
            ItemCode = "ITM_VIEJO",
            Quantity = 1m,
            WhsCode = "01",
            LineNum = 0,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageInboundWriter(contexto);
        await writer.EscribirAsync(companyId, [CrearRegistroTraslado(500123, new DateTime(2026, 8, 15))], CancellationToken.None);

        var hdr = Assert.Single(contexto.WmsSapStageInboundHdrs);
        Assert.Equal(WmsSapStageStatus.Pendiente, hdr.Status);
        Assert.Null(hdr.ErrorMsg);

        var dtl = Assert.Single(contexto.WmsSapStageInboundDtls);
        Assert.Equal("ITM001", dtl.ItemCode);
    }
}
