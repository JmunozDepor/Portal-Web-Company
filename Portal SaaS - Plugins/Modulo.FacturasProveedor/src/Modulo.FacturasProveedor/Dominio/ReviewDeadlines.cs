namespace Modulo.FacturasProveedor.Dominio;

/// <summary>
/// Fechas del plazo legal (Ley 19.983: 8 días corridos desde la recepción en el SII, parametrizable). Todas en hora de
/// Chile: nowChile es la hora local de Chile (ChileClock).
/// </summary>
public static class ReviewDeadlines
{
    /// <summary>Último día para responder: recepción + días del plazo.</summary>
    public static DateOnly LegalDeadline(DateOnly siiReceivedOn, int legalDays) => siiReceivedOn.AddDays(legalDays);

    /// <summary>Fin del aviso (avisar y rechazar): recepción + días de aviso, nunca después del penúltimo día del plazo.</summary>
    public static DateOnly WarnDeadline(DateOnly siiReceivedOn, int warnDays, DateOnly legalDeadline)
    {
        var warn = siiReceivedOn.AddDays(warnDays);
        var latest = legalDeadline.AddDays(-1);
        return warn < latest ? warn : latest;
    }

    /// <summary>Venció: el día ya pasó, o es hoy y ya pasó la hora de corte.</summary>
    public static bool IsDue(DateOnly dueDate, TimeOnly cutoff, DateTime nowChile)
    {
        var today = DateOnly.FromDateTime(nowChile);
        return today > dueDate || (today == dueDate && TimeOnly.FromDateTime(nowChile) >= cutoff);
    }

    public static bool ShouldEscalate(DateOnly legalDeadline, int escalateDaysBefore, DateTime nowChile) =>
        DateOnly.FromDateTime(nowChile) >= legalDeadline.AddDays(-escalateDaysBefore);
}
