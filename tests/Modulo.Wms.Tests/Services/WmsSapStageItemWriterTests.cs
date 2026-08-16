using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageItemWriterTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    [Fact]
    public async Task EscribirAsync_ItemNuevo_InsertaPendiente()
    {
        var contexto = CrearContexto();
        var writer = new WmsSapStageItemWriter(contexto);
        var companyId = Guid.NewGuid();

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["ItemCode"] = "ITM001",
            ["ItemName"] = "Artículo de prueba",
            ["BarCode"] = "7801234567890",
            ["SourceUpdateDate"] = new DateTime(2026, 8, 15),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        var fila = Assert.Single(contexto.WmsSapStageItems);
        Assert.Equal("ITM001", fila.ItemCode);
        Assert.Equal(WmsSapStageStatus.Pendiente, fila.Status);
        Assert.Equal(companyId, fila.CompanyId);
    }

    [Fact]
    public async Task EscribirAsync_ItemYaPendienteSinCambios_NoDuplica()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        contexto.WmsSapStageItems.Add(new WmsSapStageItem
        {
            CompanyId = companyId,
            ItemCode = "ITM001",
            ItemName = "Artículo de prueba",
            SourceUpdateDate = new DateTime(2026, 8, 15),
            Status = WmsSapStageStatus.Pendiente,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageItemWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["ItemCode"] = "ITM001",
            ["ItemName"] = "Artículo de prueba",
            ["BarCode"] = null,
            ["SourceUpdateDate"] = new DateTime(2026, 8, 15),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        Assert.Single(contexto.WmsSapStageItems);
    }

    [Fact]
    public async Task EscribirAsync_ItemYaProcesadoConCambioEnSap_VuelveAPendiente()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        contexto.WmsSapStageItems.Add(new WmsSapStageItem
        {
            CompanyId = companyId,
            ItemCode = "ITM001",
            ItemName = "Nombre viejo",
            SourceUpdateDate = new DateTime(2026, 8, 10),
            Status = WmsSapStageStatus.ProcesadoWms,
            SyncedAt = DateTimeOffset.UtcNow,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageItemWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["ItemCode"] = "ITM001",
            ["ItemName"] = "Nombre nuevo",
            ["BarCode"] = null,
            ["SourceUpdateDate"] = new DateTime(2026, 8, 15),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        var fila = Assert.Single(contexto.WmsSapStageItems);
        Assert.Equal(WmsSapStageStatus.Pendiente, fila.Status);
        Assert.Equal("Nombre nuevo", fila.ItemName);
    }

    [Fact]
    public async Task EscribirAsync_ItemEnErrorWms_VuelveAPendienteYLimpiaErrorMsg()
    {
        // Bug real encontrado en la revisión final de Ronda C: a diferencia de
        // WmsSapStageInboundWriter (Traslado), este writer nunca disparaba resync
        // desde ErrorWms -- un Artículo que fallara una vez al postear a WMS quedaba
        // en error para siempre, sin ningún reintento posterior.
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        contexto.WmsSapStageItems.Add(new WmsSapStageItem
        {
            CompanyId = companyId,
            ItemCode = "ITM001",
            ItemName = "Artículo con error",
            SourceUpdateDate = new DateTime(2026, 8, 10),
            Status = WmsSapStageStatus.ErrorWms,
            ErrorMsg = "Error al postear en WMS",
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageItemWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["ItemCode"] = "ITM001",
            ["ItemName"] = "Artículo con error",
            ["BarCode"] = null,
            // Misma fecha que ya tenía -- SAP puede reenviar el mismo dato, el punto
            // es que ErrorWms siempre debe reintentarse sin importar la fecha.
            ["SourceUpdateDate"] = new DateTime(2026, 8, 10),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        var fila = Assert.Single(contexto.WmsSapStageItems);
        Assert.Equal(WmsSapStageStatus.Pendiente, fila.Status);
        Assert.Null(fila.ErrorMsg);
    }
}
