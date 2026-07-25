using B1SLayer;
using PortalSaas.Core.Sap;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Cachea una SLConnection por clave (usuario TÉCNICO/de integración de la compañía, no
/// depende del usuario web conectado) -- Singleton. B1SLayer maneja el relogin/refresh de
/// token internamente. Portado de PortalSAP_v2 (ISapSessionCache/SapSessionCache), tal
/// cual salvo la clave de cache: acá es directamente Company.Id (Guid, ya único por
/// compañía+base de datos) en vez de "empresaCodigo:database" (dos strings compuestos).
/// </summary>
public interface ISapSessionCache
{
    Task<SLConnection> GetOrCreateAsync(Guid companyId, Func<Task<SLConnection>> create);
}

public sealed class SapSessionCache : ISapSessionCache
{
    private readonly Dictionary<Guid, SLConnection> _sessions = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<SLConnection> GetOrCreateAsync(Guid companyId, Func<Task<SLConnection>> create)
    {
        if (_sessions.TryGetValue(companyId, out var existing))
        {
            return existing;
        }

        await _lock.WaitAsync();
        try
        {
            if (_sessions.TryGetValue(companyId, out existing))
            {
                return existing;
            }

            var connection = await create();
            _sessions[companyId] = connection;
            return connection;
        }
        finally
        {
            _lock.Release();
        }
    }
}
