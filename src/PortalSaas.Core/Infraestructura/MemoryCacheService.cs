using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.Infraestructura;

/// <summary>Ver ICacheService para el criterio de qué cachear acá y qué no.</summary>
public sealed class MemoryCacheService(IMemoryCache cache) : ICacheService
{
    // IMemoryCache no expone enumeración de claves -- se lleva un registro propio
    // para poder implementar RemoveByPrefix (ej. invalidar "modulos:*" de una
    // organización sin conocer cada clave exacta de antemano).
    private readonly ConcurrentDictionary<string, byte> _knownKeys = new();

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl)
    {
        if (cache.TryGetValue(key, out T? cached))
        {
            return cached!;
        }

        var value = await factory();
        cache.Set(key, value, ttl);
        _knownKeys.TryAdd(key, 0);
        return value;
    }

    public void Remove(string key)
    {
        cache.Remove(key);
        _knownKeys.TryRemove(key, out _);
    }

    public void RemoveByPrefix(string prefix)
    {
        foreach (var key in _knownKeys.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            Remove(key);
        }
    }
}
