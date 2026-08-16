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
    public async Task EscribirAsync_StoreNuevo_InsertaPendiente()
    {
        var contexto = CrearContexto();
        var writer = new WmsSapStageStoreWriter(contexto);
        var companyId = Guid.NewGuid();

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["CardCode"] = "C001",
            ["CardName"] = "Tienda de prueba",
            ["Street"] = "Calle 123",
            ["City"] = "Santiago",
            ["ZipCode"] = "1000000",
            ["SourceUpdateDate"] = new DateTime(2026, 8, 15),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        var fila = Assert.Single(contexto.WmsSapStageStores);
        Assert.Equal("C001", fila.CardCode);
        Assert.Equal(WmsSapStageStatus.Pendiente, fila.Status);
        Assert.Equal(companyId, fila.CompanyId);
    }

    [Fact]
    public async Task EscribirAsync_StoreYaPendienteSinCambios_NoDuplica()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        contexto.WmsSapStageStores.Add(new WmsSapStageStore
        {
            CompanyId = companyId,
            CardCode = "C001",
            CardName = "Tienda de prueba",
            SourceUpdateDate = new DateTime(2026, 8, 15),
            Status = WmsSapStageStatus.Pendiente,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageStoreWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["CardCode"] = "C001",
            ["CardName"] = "Tienda de prueba",
            ["Street"] = null,
            ["City"] = null,
            ["ZipCode"] = null,
            ["SourceUpdateDate"] = new DateTime(2026, 8, 15),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        Assert.Single(contexto.WmsSapStageStores);
    }

    [Fact]
    public async Task EscribirAsync_StoreYaProcesadoConCambioEnSap_VuelveAPendiente()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        contexto.WmsSapStageStores.Add(new WmsSapStageStore
        {
            CompanyId = companyId,
            CardCode = "C001",
            CardName = "Nombre viejo",
            SourceUpdateDate = new DateTime(2026, 8, 10),
            Status = WmsSapStageStatus.ProcesadoWms,
            SyncedAt = DateTimeOffset.UtcNow,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageStoreWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["CardCode"] = "C001",
            ["CardName"] = "Nombre nuevo",
            ["Street"] = null,
            ["City"] = null,
            ["ZipCode"] = null,
            ["SourceUpdateDate"] = new DateTime(2026, 8, 15),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        var fila = Assert.Single(contexto.WmsSapStageStores);
        Assert.Equal(WmsSapStageStatus.Pendiente, fila.Status);
        Assert.Equal("Nombre nuevo", fila.CardName);
    }
}
