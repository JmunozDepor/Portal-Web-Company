using PortalSaas.Core.Infraestructura;
using Xunit;

namespace PortalSaas.Core.Tests.Infraestructura;

public class SapSessionCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_dos_compañias_distintas_no_se_serializan_entre_si()
    {
        var cache = new SapSessionCache();
        var company1Started = new TaskCompletionSource();
        var company2Started = new TaskCompletionSource();
        var releaseGate = new TaskCompletionSource();

        // Simula dos creaciones "lentas" en paralelo, cada una espera a que la OTRA
        // haya arrancado antes de terminar -- si el lock fuera global (bug original),
        // esto hace deadlock porque la segunda nunca llega a arrancar mientras la
        // primera sigue dentro del semáforo compartido.
        var task1 = cache.GetOrCreateAsync(Guid.NewGuid(), async () =>
        {
            company1Started.SetResult();
            await company2Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return null!;
        });

        var task2 = cache.GetOrCreateAsync(Guid.NewGuid(), async () =>
        {
            company2Started.SetResult();
            await company1Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return null!;
        });

        var completed = await Task.WhenAll(task1, task2).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, completed.Length);
    }
}
