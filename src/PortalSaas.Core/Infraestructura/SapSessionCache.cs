using System.Collections.Concurrent;
using B1SLayer;
using PortalSaas.Core.Sap;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Cachea una SLConnection por clave (usuario TÉCNICO/de integración de la compañía, no
/// depende del usuario web conectado) -- Singleton. B1SLayer maneja el relogin/refresh de
/// token internamente.
///
/// Semáforo POR COMPAÑÍA (no uno global) -- con un solo semáforo compartido, un
/// cache-miss simultáneo de dos compañías distintas serializaba su creación de sesión
/// sin necesidad (cuello de botella real en arranque en frío de un web farm con
/// muchas organizaciones, ver docs/superpowers/plans). Cada companyId tiene su propio
/// lock, así que compañías distintas nunca se bloquean entre sí.
/// </summary>
public interface ISapSessionCache
{
    Task<SLConnection> GetOrCreateAsync(Guid companyId, Func<Task<SLConnection>> create);
}

public sealed class SapSessionCache : ISapSessionCache
{
    private readonly ConcurrentDictionary<Guid, SLConnection> _sessions = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<SLConnection> GetOrCreateAsync(Guid companyId, Func<Task<SLConnection>> create)
    {
        if (_sessions.TryGetValue(companyId, out var existing))
        {
            return existing;
        }

        var companyLock = _locks.GetOrAdd(companyId, _ => new SemaphoreSlim(1, 1));
        await companyLock.WaitAsync();
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
            companyLock.Release();
        }
    }
}
