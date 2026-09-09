using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class ExternalServiceUsageService : IExternalServiceUsageService
{
    /// <summary>
    /// Offset de la hora del Pacífico que usa Google para reiniciar el cupo diario
    /// gratuito (medianoche UTC-8). Offset fijo a propósito -- no se ajusta por horario
    /// de verano (PDT): el contador es un guardrail de presupuesto, no facturación, y
    /// una hora de corrimiento en el corte no cambia el resultado. El límite real lo
    /// sigue imponiendo Google (HTTP 429 al pasarse).
    /// </summary>
    private static readonly TimeSpan PacificOffset = TimeSpan.FromHours(-8);

    private readonly RendicionesDbContext _db;

    public ExternalServiceUsageService(RendicionesDbContext db)
    {
        _db = db;
    }

    public async Task<bool> TryReserveAsync(long providerId, int quantity, int periodLimit, string quotaPeriod, CancellationToken ct = default)
    {
        var bucket = CurrentBucket(quotaPeriod);
        await EnsureRowExistsAsync(providerId, bucket, ct);

        // ExecuteUpdateAsync traduce a un único UPDATE condicionado en la base --
        // portable entre Postgres y SQL Server, EF Core lo resuelve por proveedor.
        var filasAfectadas = await _db.ExternalServiceUsages
            .Where(u => u.ProviderId == providerId && u.Year == bucket.Year && u.Month == bucket.Month && u.Day == bucket.Day
                && u.UsedUnits + quantity <= periodLimit)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.UsedUnits, u => u.UsedUnits + quantity), ct);

        return filasAfectadas > 0;
    }

    public async Task RecordAsync(long providerId, int quantity, string quotaPeriod, CancellationToken ct = default)
    {
        var bucket = CurrentBucket(quotaPeriod);
        await EnsureRowExistsAsync(providerId, bucket, ct);

        await _db.ExternalServiceUsages
            .Where(u => u.ProviderId == providerId && u.Year == bucket.Year && u.Month == bucket.Month && u.Day == bucket.Day)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.UsedUnits, u => u.UsedUnits + quantity), ct);
    }

    public async Task<int> GetCurrentUsageAsync(long providerId, string quotaPeriod, CancellationToken ct = default)
    {
        var bucket = CurrentBucket(quotaPeriod);
        return await _db.ExternalServiceUsages
            .AsNoTracking()
            .Where(u => u.ProviderId == providerId && u.Year == bucket.Year && u.Month == bucket.Month && u.Day == bucket.Day)
            .Select(u => u.UsedUnits)
            .FirstOrDefaultAsync(ct);
    }

    private async Task EnsureRowExistsAsync(long providerId, (int Year, int Month, int Day) bucket, CancellationToken ct)
    {
        var existe = await _db.ExternalServiceUsages
            .AsNoTracking()
            .AnyAsync(u => u.ProviderId == providerId && u.Year == bucket.Year && u.Month == bucket.Month && u.Day == bucket.Day, ct);
        if (existe)
            return;

        // Ventana de carrera teórica bajo alta concurrencia (dos requests creando la
        // fila del mismo período a la vez) -- aceptable para el volumen esperado. El
        // UPDATE condicionado de arriba sigue siendo atómico una vez que la fila
        // existe, que es lo que evita sobrepasar el límite.
        _db.ExternalServiceUsages.Add(new ExternalServiceUsage
        {
            ProviderId = providerId,
            Year = bucket.Year,
            Month = bucket.Month,
            Day = bucket.Day,
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

    /// <summary>
    /// Balde de consumo vigente: mes (day = 0) para cuota mensual; año/mes/día en hora
    /// del Pacífico para cuota diaria (Gemini).
    /// </summary>
    private static (int Year, int Month, int Day) CurrentBucket(string quotaPeriod)
    {
        if (quotaPeriod == QuotaPeriods.Daily)
        {
            var pacific = DateTimeOffset.UtcNow.ToOffset(PacificOffset);
            return (pacific.Year, pacific.Month, pacific.Day);
        }

        var utc = DateTimeOffset.UtcNow;
        return (utc.Year, utc.Month, 0);
    }
}
