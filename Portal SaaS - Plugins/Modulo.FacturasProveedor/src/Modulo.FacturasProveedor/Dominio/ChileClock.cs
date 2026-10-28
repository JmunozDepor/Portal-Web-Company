namespace Modulo.FacturasProveedor.Dominio;

/// <summary>
/// Hora de negocio en zona America/Santiago (fallback Windows "Pacific SA Standard Time").
/// La base guarda siempre UTC; los recargos por hora se evalúan en hora Chile.
/// </summary>
public static class ChileClock
{
    private static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("America/Santiago", out var tz)) return tz;
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Pacific SA Standard Time", out tz)) return tz;
        throw new TimeZoneNotFoundException("No se encontró la zona horaria de Chile.");
    }

    public static DateTime Now => ToLocal(DateTime.UtcNow);

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static DateTime ToUtc(DateTime local) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone), DateTimeKind.Utc);
}
