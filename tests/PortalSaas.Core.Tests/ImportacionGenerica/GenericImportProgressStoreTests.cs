using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica;
using PortalSaas.Core.Tests.TestHelpers;
using PortalSaas.Data;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

public class GenericImportProgressStoreTests
{
    private static PortalSaasDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PortalSaasDbContext(options, new NullOrganizationScopeProvider());
    }

    [Fact]
    public async Task Update_persiste_en_la_base_no_en_memoria_de_proceso()
    {
        var dbName = Guid.NewGuid().ToString();
        var progress = new GenericImportProgressDto
        {
            TotalRows = 100,
            ProcessedRows = 40,
            Status = "En progreso",
        };

        // Escribe con una instancia del store, sobre un DbContext propio --
        // simula una instancia de la app distinta a la que va a leer.
        await using (var writerContext = CreateContext(dbName))
        {
            var writerStore = new GenericImportProgressStore(writerContext);
            await writerStore.UpdateAsync("job-1", progress);
        }

        // Lee con OTRA instancia del store y OTRO DbContext contra la misma base --
        // si el progreso viviera solo en memoria de proceso (ConcurrentDictionary),
        // esto devolvería null.
        await using var readerContext = CreateContext(dbName);
        var readerStore = new GenericImportProgressStore(readerContext);
        var result = await readerStore.GetAsync("job-1");

        Assert.NotNull(result);
        Assert.Equal(100, result!.TotalRows);
        Assert.Equal(40, result.ProcessedRows);
        Assert.Equal("En progreso", result.Status);
    }

    [Fact]
    public async Task GetAsync_con_jobId_inexistente_devuelve_null()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var store = new GenericImportProgressStore(context);

        var result = await store.GetAsync("no-existe");

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_sobre_el_mismo_jobId_actualiza_en_vez_de_duplicar()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        var store = new GenericImportProgressStore(context);

        await store.UpdateAsync("job-2", new GenericImportProgressDto { TotalRows = 10, ProcessedRows = 1, Status = "Inicio" });
        await store.UpdateAsync("job-2", new GenericImportProgressDto { TotalRows = 10, ProcessedRows = 10, Status = "Completado" });

        var result = await store.GetAsync("job-2");

        Assert.NotNull(result);
        Assert.Equal(10, result!.ProcessedRows);
        Assert.Equal("Completado", result.Status);
        Assert.Single(context.Set<PortalSaas.Data.Entities.GenericImportJobProgress>());
    }
}
