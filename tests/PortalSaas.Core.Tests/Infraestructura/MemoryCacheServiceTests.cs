using Microsoft.Extensions.Caching.Memory;
using PortalSaas.Core.Infraestructura;
using Xunit;

namespace PortalSaas.Core.Tests.Infraestructura;

public class MemoryCacheServiceTests
{
    private static MemoryCacheService CreateService() => new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task GetOrCreateAsync_segunda_llamada_no_ejecuta_la_factory_de_nuevo()
    {
        var cache = CreateService();
        var calls = 0;

        async Task<int> Factory()
        {
            calls++;
            await Task.CompletedTask;
            return 42;
        }

        var first = await cache.GetOrCreateAsync("clave-1", Factory, TimeSpan.FromMinutes(5));
        var second = await cache.GetOrCreateAsync("clave-1", Factory, TimeSpan.FromMinutes(5));

        Assert.Equal(42, first);
        Assert.Equal(42, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Remove_invalida_la_clave_y_la_siguiente_llamada_reejecuta_la_factory()
    {
        var cache = CreateService();
        var calls = 0;

        async Task<int> Factory()
        {
            calls++;
            await Task.CompletedTask;
            return calls;
        }

        await cache.GetOrCreateAsync("clave-2", Factory, TimeSpan.FromMinutes(5));
        cache.Remove("clave-2");
        var afterRemove = await cache.GetOrCreateAsync("clave-2", Factory, TimeSpan.FromMinutes(5));

        Assert.Equal(2, calls);
        Assert.Equal(2, afterRemove);
    }

    [Fact]
    public async Task RemoveByPrefix_invalida_solo_las_claves_que_empiezan_con_ese_prefijo()
    {
        var cache = CreateService();

        await cache.GetOrCreateAsync("modulos:org-1", () => Task.FromResult(1), TimeSpan.FromMinutes(5));
        await cache.GetOrCreateAsync("modulos:org-2", () => Task.FromResult(2), TimeSpan.FromMinutes(5));
        await cache.GetOrCreateAsync("otro:dato", () => Task.FromResult(3), TimeSpan.FromMinutes(5));

        cache.RemoveByPrefix("modulos:");

        var calls = 0;
        var org1AfterRemove = await cache.GetOrCreateAsync("modulos:org-1", () => { calls++; return Task.FromResult(99); }, TimeSpan.FromMinutes(5));
        var otroAfterRemove = await cache.GetOrCreateAsync("otro:dato", () => { calls++; return Task.FromResult(99); }, TimeSpan.FromMinutes(5));

        Assert.Equal(99, org1AfterRemove); // se reejecutó, valor nuevo
        Assert.Equal(3, otroAfterRemove);  // no se tocó, sigue el valor cacheado
        Assert.Equal(1, calls);
    }
}
