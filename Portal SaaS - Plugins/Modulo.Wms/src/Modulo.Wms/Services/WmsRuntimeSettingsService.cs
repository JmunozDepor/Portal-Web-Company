using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Modulo.Wms.Data;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public sealed class WmsRuntimeSettingsService : IWmsRuntimeSettingsService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private const string CachePrefix = "wms_rt:";

    private readonly WmsDbContext _db;
    private readonly IMemoryCache _cache;

    public WmsRuntimeSettingsService(WmsDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<int> GetIntAsync(string clave, int codeDefault, CancellationToken ct = default)
    {
        var cacheKey = CachePrefix + clave;
        if (_cache.TryGetValue(cacheKey, out int cached))
        {
            return cached;
        }

        var fila = await _db.RuntimeSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Clave == clave, ct);

        var valor = fila is not null && int.TryParse(fila.Valor, out var parsed) ? parsed : codeDefault;
        _cache.Set(cacheKey, valor, CacheTtl);
        return valor;
    }

    public async Task<IReadOnlyList<(WmsRuntimeSettingKey Meta, int ValorEfectivo, bool EsDefault)>> ListarAsync(CancellationToken ct = default)
    {
        var filas = await _db.RuntimeSettings.AsNoTracking().ToListAsync(ct);
        var porClave = filas.ToDictionary(f => f.Clave, StringComparer.Ordinal);

        return WmsRuntimeSettingsKeys.Todas
            .Select(meta =>
            {
                var tieneFila = porClave.TryGetValue(meta.Clave, out var fila)
                    && int.TryParse(fila!.Valor, out _);
                var valor = tieneFila ? int.Parse(porClave[meta.Clave].Valor) : meta.Default;
                return (meta, valor, !tieneFila);
            })
            .ToList();
    }

    public async Task GuardarAsync(string clave, string valor, string updatedBy, CancellationToken ct = default)
    {
        var meta = WmsRuntimeSettingsKeys.Todas.FirstOrDefault(k => k.Clave == clave)
            ?? throw new InvalidOperationException("Clave de ajuste no reconocida.");

        if (!int.TryParse(valor, out var entero) || entero < 0)
        {
            throw new InvalidOperationException($"'{meta.Etiqueta}' requiere un número entero mayor o igual a 0.");
        }

        var fila = await _db.RuntimeSettings.FirstOrDefaultAsync(s => s.Clave == clave, ct);
        if (fila is null)
        {
            fila = new WmsRuntimeSetting { Clave = clave };
            _db.RuntimeSettings.Add(fila);
        }

        fila.Valor = entero.ToString();
        fila.UpdatedAt = DateTimeOffset.UtcNow;
        fila.UpdatedBy = updatedBy;

        await _db.SaveChangesAsync(ct);
        _cache.Remove(CachePrefix + clave);
    }

    public async Task RestaurarDefaultAsync(string clave, CancellationToken ct = default)
    {
        var fila = await _db.RuntimeSettings.FirstOrDefaultAsync(s => s.Clave == clave, ct);
        if (fila is not null)
        {
            _db.RuntimeSettings.Remove(fila);
            await _db.SaveChangesAsync(ct);
        }
        _cache.Remove(CachePrefix + clave);
    }
}
