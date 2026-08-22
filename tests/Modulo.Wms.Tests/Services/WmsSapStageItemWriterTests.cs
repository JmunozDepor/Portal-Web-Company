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
        await using var contexto = CrearContexto();
        var writer = new WmsSapStageItemWriter(contexto);
        var companyId = Guid.NewGuid();

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["item_alternate_code"] = "ITM001",
            ["description"] = "Artículo de prueba",
            ["barcode"] = "7801234567890",
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
        await using var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        contexto.WmsSapStageItems.Add(new WmsSapStageItem
        {
            CompanyId = companyId,
            ItemCode = "ITM001",
            ItemName = "Artículo de prueba",
            Status = WmsSapStageStatus.Pendiente,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageItemWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["item_alternate_code"] = "ITM001",
            ["description"] = "Artículo de prueba",
            ["barcode"] = null,
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        Assert.Single(contexto.WmsSapStageItems);
    }

    [Fact]
    public async Task EscribirAsync_ItemProcesadoSinCambioDeValor_NoVuelveAPendiente()
    {
        // Con el diff por valor (vs. el viejo cursor de fecha), un reenvío de SAP sin
        // cambios reales de datos no debe disparar reprocesamiento.
        await using var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        contexto.WmsSapStageItems.Add(new WmsSapStageItem
        {
            CompanyId = companyId,
            ItemCode = "ITM001",
            ItemName = "Artículo de prueba",
            BarCode = "7801234567890",
            Status = WmsSapStageStatus.ProcesadoWms,
            SyncedAt = DateTimeOffset.UtcNow,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageItemWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["item_alternate_code"] = "ITM001",
            ["description"] = "Artículo de prueba",
            ["barcode"] = "7801234567890",
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        var fila = Assert.Single(contexto.WmsSapStageItems);
        Assert.Equal(WmsSapStageStatus.ProcesadoWms, fila.Status);
    }

    [Fact]
    public async Task EscribirAsync_ItemEnErrorWms_VuelveAPendienteYLimpiaErrorMsg()
    {
        // Bug real encontrado en la revisión final de Ronda C: a diferencia de
        // WmsSapStageInboundWriter (Traslado), este writer nunca disparaba resync
        // desde ErrorWms -- un Artículo que fallara una vez al postear a WMS quedaba
        // en error para siempre, sin ningún reintento posterior.
        await using var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        contexto.WmsSapStageItems.Add(new WmsSapStageItem
        {
            CompanyId = companyId,
            ItemCode = "ITM001",
            ItemName = "Artículo con error",
            Status = WmsSapStageStatus.ErrorWms,
            ErrorMsg = "Error al postear en WMS",
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageItemWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["item_alternate_code"] = "ITM001",
            ["description"] = "Artículo con error",
            ["barcode"] = null,
            // Mismos datos que ya tenía -- ErrorWms siempre debe reintentarse sin
            // importar si hubo cambio de valor.
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        var fila = Assert.Single(contexto.WmsSapStageItems);
        Assert.Equal(WmsSapStageStatus.Pendiente, fila.Status);
        Assert.Null(fila.ErrorMsg);
    }

    [Fact]
    public async Task EscribirAsync_CambiaCampoQueNoEstaEnValidationFields_NoMarcaPendiente()
    {
        var companyId = Guid.NewGuid();
        await using var contexto = CrearContexto();
        contexto.ValidationFields.Add(new WmsValidationField { CompanyId = companyId, TipoEntidad = "Item", FieldName = "brand_code", IsActive = true });
        contexto.WmsSapStageItems.Add(new WmsSapStageItem
        {
            CompanyId = companyId, ItemCode = "ITM1", ItemName = "Original", BarCode = "123",
            Status = WmsSapStageStatus.ProcesadoWms,
            ExtraFieldsJson = """{"brand_code":"NIKE","unit_length":30}""",
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageItemWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["item_alternate_code"] = "ITM1", ["description"] = "Original", ["barcode"] = "123",
            ["brand_code"] = "NIKE", ["unit_length"] = 45, // cambia unit_length, NO está en ValidationFields
        });

        await writer.EscribirAsync(companyId, new[] { registro }, CancellationToken.None);

        var fila = await contexto.WmsSapStageItems.SingleAsync(f => f.ItemCode == "ITM1");
        Assert.Equal(WmsSapStageStatus.ProcesadoWms, fila.Status); // NO cambió a Pendiente
        Assert.Contains("\"unit_length\":45", fila.ExtraFieldsJson); // pero el dato SÍ se actualizó
    }

    [Fact]
    public async Task EscribirAsync_CambiaCampoQueEstaEnValidationFields_MarcaPendiente()
    {
        var companyId = Guid.NewGuid();
        await using var contexto = CrearContexto();
        contexto.ValidationFields.Add(new WmsValidationField { CompanyId = companyId, TipoEntidad = "Item", FieldName = "brand_code", IsActive = true });
        contexto.WmsSapStageItems.Add(new WmsSapStageItem
        {
            CompanyId = companyId, ItemCode = "ITM1", ItemName = "Original", BarCode = "123",
            Status = WmsSapStageStatus.ProcesadoWms,
            ExtraFieldsJson = """{"brand_code":"NIKE"}""",
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageItemWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["item_alternate_code"] = "ITM1", ["description"] = "Original", ["barcode"] = "123",
            ["brand_code"] = "NIKE-KIDS", // SÍ está en ValidationFields y cambió
        });

        await writer.EscribirAsync(companyId, new[] { registro }, CancellationToken.None);

        var fila = await contexto.WmsSapStageItems.SingleAsync(f => f.ItemCode == "ITM1");
        Assert.Equal(WmsSapStageStatus.Pendiente, fila.Status);
    }
}
