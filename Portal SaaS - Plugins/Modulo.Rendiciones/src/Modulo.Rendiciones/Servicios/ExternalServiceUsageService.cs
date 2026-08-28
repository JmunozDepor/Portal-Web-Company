using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class ExternalServiceUsageService : IExternalServiceUsageService
{
    private readonly RendicionesDbContext _db;

    public ExternalServiceUsageService(RendicionesDbContext db)
    {
        _db = db;
    }

    public async Task<bool> TryReserveAsync(long providerId, int quantity, int monthlyLimit, CancellationToken ct = default)
    {
        await EnsureRowExistsAsync(providerId, ct);

        // ExecuteUpdateAsync traduce a un único UPDATE condicionado en la base --
        // portable entre Postgres y SQL Server, EF Core lo resuelve por proveedor.
        var (year, month) = CurrentPeriod();
        var filasAfectadas = await _db.ExternalServiceUsages
            .Where(u => u.ProviderId == providerId && u.Year == year && u.Month == month
                && u.UsedUnits + quantity <= monthlyLimit)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.UsedUnits, u => u.UsedUnits + quantity), ct);

        return filasAfectadas > 0;
    }

    public async Task RecordAsync(long providerId, int quantity, CancellationToken ct = default)
    {
        await EnsureRowExistsAsync(providerId, ct);

        var (year, month) = CurrentPeriod();
        await _db.ExternalServiceUsages
            .Where(u => u.ProviderId == providerId && u.Year == year && u.Month == month)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.UsedUnits, u => u.UsedUnits + quantity), ct);
    }

    public async Task<int> GetCurrentMonthUsageAsync(long providerId, CancellationToken ct = default)
    {
        var (year, month) = CurrentPeriod();
        return await _db.ExternalServiceUsages
            .AsNoTracking()
            .Where(u => u.ProviderId == providerId && u.Year == year && u.Month == month)
            .Select(u => u.UsedUnits)
            .FirstOrDefaultAsync(ct);
    }

    private async Task EnsureRowExistsAsync(long providerId, CancellationToken ct)
    {
        var (year, month) = CurrentPeriod();
        var existe = await _db.ExternalServiceUsages
            .AsNoTracking()
            .AnyAsync(u => u.ProviderId == providerId && u.Year == year && u.Month == month, ct);
        if (existe)
            return;

        // Ventana de carrera teórica bajo alta concurrencia (dos requests creando la
        // fila del mismo período a la vez) -- aceptable para el volumen esperado. El
        // UPDATE condicionado de arriba sigue siendo atómico una vez que la fila
        // existe, que es lo que evita sobrepasar el límite.
        _db.ExternalServiceUsages.Add(new ExternalServiceUsage
        {
            ProviderId = providerId,
            Year = year,
            Month = month,
            UsedUnits = 0,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Otro request ganó la carrera y ya creó la fila -- no es un error real.
        }
    }

    private static (int Year, int Month) CurrentPeriod()
    {
        var now = DateTimeOffset.UtcNow;
        return (now.Year, now.Month);
    }
}
