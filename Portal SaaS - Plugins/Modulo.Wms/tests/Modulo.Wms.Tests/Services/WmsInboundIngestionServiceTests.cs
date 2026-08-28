using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsInboundIngestionServiceTests
{
    private static WmsDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(options);
    }

    [Fact]
    public async Task InsertPendingAsync_ConDatosNuevos_InsertaYRetornaInsertadoTrue()
    {
        await using var contexto = CrearContexto();
        var servicio = new WmsInboundIngestionService(contexto);
        var companyId = Guid.NewGuid();

        var resultado = await servicio.InsertPendingAsync(
            companyId, "SLSH", "xml", "test.xml", "hash-unico-1", "<xml/>", CancellationToken.None);

        Assert.True(resultado.Insertado);
        Assert.False(resultado.Duplicado);
        var fila = await contexto.WmsOracleInboundStages.FirstAsync();
        Assert.Equal(companyId, fila.CompanyId);
        Assert.Equal("SLSH", fila.TipoDoc);
    }

    [Fact]
    public async Task InsertPendingAsync_ConHashDuplicadoParaLaMismaCompany_NoInsertaDeNuevo()
    {
        await using var contexto = CrearContexto();
        var servicio = new WmsInboundIngestionService(contexto);
        var companyId = Guid.NewGuid();
        await servicio.InsertPendingAsync(companyId, "SLSH", "xml", "test.xml", "hash-repetido", "<xml/>", CancellationToken.None);

        var resultado = await servicio.InsertPendingAsync(companyId, "SLSH", "xml", "test2.xml", "hash-repetido", "<xml/>", CancellationToken.None);

        Assert.False(resultado.Insertado);
        Assert.True(resultado.Duplicado);
        Assert.Equal(1, await contexto.WmsOracleInboundStages.CountAsync());
    }

    [Fact]
    public async Task InsertPendingAsync_ConMismoHashEnCompaniasDistintas_InsertaAmbas()
    {
        await using var contexto = CrearContexto();
        var servicio = new WmsInboundIngestionService(contexto);

        await servicio.InsertPendingAsync(Guid.NewGuid(), "SLSH", "xml", "a.xml", "hash-compartido", "<xml/>", CancellationToken.None);
        var resultado = await servicio.InsertPendingAsync(Guid.NewGuid(), "SLSH", "xml", "b.xml", "hash-compartido", "<xml/>", CancellationToken.None);

        Assert.True(resultado.Insertado);
        Assert.Equal(2, await contexto.WmsOracleInboundStages.CountAsync());
    }
}
