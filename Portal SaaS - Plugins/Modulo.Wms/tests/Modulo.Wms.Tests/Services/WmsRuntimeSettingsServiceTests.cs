using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Modulo.Wms.Data;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsRuntimeSettingsServiceTests
{
    private static (WmsRuntimeSettingsService Svc, WmsDbContext Ctx) Crear()
    {
        var ctx = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        return (new WmsRuntimeSettingsService(ctx, new MemoryCache(new MemoryCacheOptions())), ctx);
    }

    [Fact]
    public async Task GetIntAsync_SinFila_DevuelveElDefaultDeCodigo()
    {
        var (svc, _) = Crear();
        var v = await svc.GetIntAsync(WmsRuntimeSettingsKeys.ExistsReconcilerMaxIntentos, 20);
        Assert.Equal(20, v);
    }

    [Fact]
    public async Task GuardarAsync_LuegoGetInt_DevuelveElValorGuardado()
    {
        var (svc, _) = Crear();
        await svc.GuardarAsync(WmsRuntimeSettingsKeys.ExistsReconcilerMaxIntentos, "7", "tester");

        var v = await svc.GetIntAsync(WmsRuntimeSettingsKeys.ExistsReconcilerMaxIntentos, 20);
        Assert.Equal(7, v);
    }

    [Fact]
    public async Task GuardarAsync_ClaveDesconocida_Rechaza()
    {
        var (svc, _) = Crear();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GuardarAsync("clave.inventada", "5", "tester"));
    }

    [Fact]
    public async Task GuardarAsync_ValorNoEntero_Rechaza()
    {
        var (svc, _) = Crear();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GuardarAsync(WmsRuntimeSettingsKeys.SlshParserMaxIntentos, "abc", "tester"));
    }

    [Fact]
    public async Task RestaurarDefaultAsync_BorraLaFila_YGetIntVuelveAlDefault()
    {
        var (svc, ctx) = Crear();
        await svc.GuardarAsync(WmsRuntimeSettingsKeys.SvshParserMaxIntentos, "9", "tester");

        await svc.RestaurarDefaultAsync(WmsRuntimeSettingsKeys.SvshParserMaxIntentos);

        Assert.Empty(ctx.RuntimeSettings);
        Assert.Equal(3, await svc.GetIntAsync(WmsRuntimeSettingsKeys.SvshParserMaxIntentos, 3));
    }

    [Fact]
    public async Task ListarAsync_MezclaFilasYDefaults()
    {
        var (svc, _) = Crear();
        await svc.GuardarAsync(WmsRuntimeSettingsKeys.StageErrorReconcilerStatusRechazado, "202", "tester");

        var lista = await svc.ListarAsync();

        Assert.Equal(WmsRuntimeSettingsKeys.Todas.Count, lista.Count);
        var rechazo = lista.Single(x => x.Meta.Clave == WmsRuntimeSettingsKeys.StageErrorReconcilerStatusRechazado);
        Assert.Equal(202, rechazo.ValorEfectivo);
        Assert.False(rechazo.EsDefault);
        var exists = lista.Single(x => x.Meta.Clave == WmsRuntimeSettingsKeys.ExistsReconcilerMaxIntentos);
        Assert.True(exists.EsDefault);
        Assert.Equal(20, exists.ValorEfectivo);
    }
}
