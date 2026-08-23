using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageStoreWriterTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    [Fact]
    public async Task EscribirAsync_StoreNuevo_InsertaPendienteConExtraFields()
    {
        var contexto = CrearContexto();
        var writer = new WmsSapStageStoreWriter(contexto);
        var companyId = Guid.NewGuid();

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["PK"] = "C001",
            ["CardName"] = "Tienda de prueba",
            ["Street"] = "Calle 123",
            ["City"] = "Santiago",
            ["ZipCode"] = "1000000",
            ["SourceUpdateDate"] = new DateTime(2026, 8, 15),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        var fila = Assert.Single(contexto.WmsSapStageStores);
        Assert.Equal("C001", fila.Pk);
        Assert.Equal(WmsSapStageStatus.Pendiente, fila.Status);
        Assert.Equal(companyId, fila.CompanyId);
        Assert.Contains("\"CardName\":\"Tienda de prueba\"", fila.ExtraFieldsJson);
        Assert.Contains("\"Street\":\"Calle 123\"", fila.ExtraFieldsJson);
        Assert.DoesNotContain("PK", fila.ExtraFieldsJson!.Replace("\"CardName\"", ""));
    }

    [Fact]
    public async Task EscribirAsync_CambiaCampoQueEstaEnValidationFields_MarcaPendiente()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        contexto.ValidationFields.Add(new WmsValidationField { CompanyId = companyId, TipoEntidad = "Store", FieldName = "City", IsActive = true });
        contexto.WmsSapStageStores.Add(new WmsSapStageStore
        {
            CompanyId = companyId,
            Pk = "C001",
            ExtraFieldsJson = """{"CardName":"Tienda","City":"Santiago"}""",
            SourceUpdateDate = new DateTime(2026, 8, 10),
            Status = WmsSapStageStatus.ProcesadoWms,
            SyncedAt = DateTimeOffset.UtcNow,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageStoreWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["PK"] = "C001",
            ["CardName"] = "Tienda",
            ["City"] = "Valparaiso", // cambió y está en ValidationFields
            ["SourceUpdateDate"] = new DateTime(2026, 8, 15),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        var fila = Assert.Single(contexto.WmsSapStageStores);
        Assert.Equal(WmsSapStageStatus.Pendiente, fila.Status);
        Assert.Contains("Valparaiso", fila.ExtraFieldsJson);
    }

    [Fact]
    public async Task EscribirAsync_CambiaCampoQueNoEstaEnValidationFields_NoMarcaPendientePeroActualizaDato()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        contexto.ValidationFields.Add(new WmsValidationField { CompanyId = companyId, TipoEntidad = "Store", FieldName = "City", IsActive = true });
        contexto.WmsSapStageStores.Add(new WmsSapStageStore
        {
            CompanyId = companyId,
            Pk = "C001",
            ExtraFieldsJson = """{"CardName":"Tienda","City":"Santiago"}""",
            SourceUpdateDate = new DateTime(2026, 8, 10),
            Status = WmsSapStageStatus.ProcesadoWms,
            SyncedAt = DateTimeOffset.UtcNow,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageStoreWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["PK"] = "C001",
            ["CardName"] = "Tienda Nueva", // cambió, NO está en ValidationFields
            ["City"] = "Santiago",
            ["SourceUpdateDate"] = new DateTime(2026, 8, 15),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        var fila = Assert.Single(contexto.WmsSapStageStores);
        Assert.Equal(WmsSapStageStatus.ProcesadoWms, fila.Status); // no cambió
        Assert.Contains("Tienda Nueva", fila.ExtraFieldsJson); // pero el dato sí se actualizó
    }

    [Fact]
    public async Task EscribirAsync_StoreEnErrorWms_VuelveAPendienteYLimpiaErrorMsg()
    {
        // Bug real encontrado en la revisión final de Ronda C: a diferencia de
        // WmsSapStageInboundWriter (Traslado), este writer nunca disparaba resync
        // desde ErrorWms -- una Tienda que fallara una vez al postear a WMS quedaba
        // en error para siempre, sin ningún reintento posterior.
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        contexto.WmsSapStageStores.Add(new WmsSapStageStore
        {
            CompanyId = companyId,
            Pk = "C001",
            ExtraFieldsJson = """{"CardName":"Tienda con error"}""",
            SourceUpdateDate = new DateTime(2026, 8, 10),
            Status = WmsSapStageStatus.ErrorWms,
            ErrorMsg = "Error al postear en WMS",
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageStoreWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["PK"] = "C001",
            ["CardName"] = "Tienda con error",
            // Misma fecha que ya tenía -- ErrorWms siempre debe reintentarse sin
            // importar la fecha ni si hubo cambio de valor.
            ["SourceUpdateDate"] = new DateTime(2026, 8, 10),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        var fila = Assert.Single(contexto.WmsSapStageStores);
        Assert.Equal(WmsSapStageStatus.Pendiente, fila.Status);
        Assert.Null(fila.ErrorMsg);
    }
}
