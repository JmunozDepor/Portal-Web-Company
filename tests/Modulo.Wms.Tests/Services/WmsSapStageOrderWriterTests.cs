using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageOrderWriterTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    private static IntegrationRecord CrearRegistroOrden(string orderNbr, DateTime sourceUpdateDate, string itemCode = "ITM001") =>
        new(new Dictionary<string, object?>
        {
            ["OrderNbr"] = orderNbr,
            ["OrderType"] = "VTA",
            ["PickListAbsEntry"] = 100,
            ["BaseObjectType"] = 17,
            ["BaseEntry"] = 500,
            ["CardCode"] = "C001",
            ["CardName"] = "Cliente de prueba",
            ["CustomerPoNbr"] = "PO-1",
            ["OrdDate"] = sourceUpdateDate,
            ["ExpDate"] = (DateTime?)null,
            ["ReqShipDate"] = (DateTime?)null,
            ["ShipToCode"] = "SHIP1",
            ["SourceUpdateDate"] = sourceUpdateDate,
            ["Lineas"] = new List<IntegrationRecord>
            {
                new(new Dictionary<string, object?>
                {
                    ["ItemCode"] = itemCode,
                    ["Quantity"] = 5m,
                    ["WhsCode"] = "01",
                    ["LineNum"] = 0,
                    ["SeqNbr"] = 1,
                }),
            },
        });

    [Fact]
    public async Task EscribirAsync_OrdenNueva_InsertaCabeceraYDetalle()
    {
        var contexto = CrearContexto();
        var writer = new WmsSapStageOrderWriter(contexto);
        var companyId = Guid.NewGuid();

        await writer.EscribirAsync(companyId, [CrearRegistroOrden("VTA-1001", new DateTime(2026, 8, 16))], CancellationToken.None);

        var hdr = Assert.Single(contexto.WmsSapStageOrderHdrs);
        Assert.Equal("VTA-1001", hdr.OrderNbr);
        Assert.Equal(WmsSapStageStatus.Pendiente, hdr.Status);

        var dtl = Assert.Single(contexto.WmsSapStageOrderDtls);
        Assert.Equal(hdr.LineId, dtl.ParentId);
        Assert.Equal("ITM001", dtl.ItemCode);
    }

    [Fact]
    public async Task EscribirAsync_OrdenYaPendienteSinCambios_NoDuplicaDetalle()
    {
        var contexto = CrearContexto();
        var writer = new WmsSapStageOrderWriter(contexto);
        var companyId = Guid.NewGuid();
        var fecha = new DateTime(2026, 8, 16);

        await writer.EscribirAsync(companyId, [CrearRegistroOrden("VTA-1001", fecha)], CancellationToken.None);
        await writer.EscribirAsync(companyId, [CrearRegistroOrden("VTA-1001", fecha)], CancellationToken.None);

        Assert.Single(contexto.WmsSapStageOrderHdrs);
        Assert.Single(contexto.WmsSapStageOrderDtls);
    }

    [Fact]
    public async Task EscribirAsync_OrdenProcesadaConCambioEnSap_VuelveAPendienteYReemplazaDetalle()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdrExistente = new WmsSapStageOrderHdr
        {
            CompanyId = companyId,
            OrderNbr = "VTA-1001",
            OrderType = "VTA",
            CardCode = "C001",
            CardName = "Cliente de prueba",
            SourceUpdateDate = new DateTime(2026, 8, 10),
            Status = WmsSapStageStatus.ProcesadoWms,
        };
        contexto.WmsSapStageOrderHdrs.Add(hdrExistente);
        await contexto.SaveChangesAsync();
        contexto.WmsSapStageOrderDtls.Add(new WmsSapStageOrderDtl { ParentId = hdrExistente.LineId, ItemCode = "ITM_VIEJO", Quantity = 1m, LineNum = 0, SeqNbr = 1 });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageOrderWriter(contexto);
        await writer.EscribirAsync(companyId, [CrearRegistroOrden("VTA-1001", new DateTime(2026, 8, 16))], CancellationToken.None);

        var hdr = Assert.Single(contexto.WmsSapStageOrderHdrs);
        Assert.Equal(WmsSapStageStatus.Pendiente, hdr.Status);

        var dtl = Assert.Single(contexto.WmsSapStageOrderDtls);
        Assert.Equal("ITM001", dtl.ItemCode);
    }

    [Fact]
    public async Task EscribirAsync_OrdenEnErrorWms_VuelveAPendienteYLimpiaError()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdrExistente = new WmsSapStageOrderHdr
        {
            CompanyId = companyId,
            OrderNbr = "VTA-1001",
            OrderType = "VTA",
            CardCode = "C001",
            CardName = "Cliente de prueba",
            SourceUpdateDate = new DateTime(2026, 8, 16),
            Status = WmsSapStageStatus.ErrorWms,
            ErrorMsg = "Rechazado por WMS",
        };
        contexto.WmsSapStageOrderHdrs.Add(hdrExistente);
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageOrderWriter(contexto);
        await writer.EscribirAsync(companyId, [CrearRegistroOrden("VTA-1001", new DateTime(2026, 8, 16))], CancellationToken.None);

        var hdr = Assert.Single(contexto.WmsSapStageOrderHdrs);
        Assert.Equal(WmsSapStageStatus.Pendiente, hdr.Status);
        Assert.Null(hdr.ErrorMsg);
    }
}
