namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>Hora de Chile para fechas tributarias (recepción en el SII, ventanas de lectura).</summary>
internal static class ChileTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    /// <summary>Fecha calendario en Chile del instante dado.</summary>
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone).DateTime);

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
