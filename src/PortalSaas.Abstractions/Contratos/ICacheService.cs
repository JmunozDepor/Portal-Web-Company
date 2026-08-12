namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Abstracción de caché por instancia -- hoy implementada sobre IMemoryCache
/// (MemoryCacheService), sin distribuido. Segura para web farm SOLO para datos de
/// lectura frecuente/escritura rara con invalidación activa desde el punto de
/// escritura (ver docs/superpowers/plans, Task 6) -- nunca usar para datos donde una
/// ventana de inconsistencia entre instancias sea inaceptable (ej. permisos de
/// seguridad por request, contadores exactos).
/// </summary>
public interface ICacheService
{
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl);
    void Remove(string key);
    void RemoveByPrefix(string prefix);
}
