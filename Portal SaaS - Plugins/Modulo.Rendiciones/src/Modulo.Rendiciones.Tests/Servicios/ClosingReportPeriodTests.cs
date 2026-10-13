using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

public class ClosingReportPeriodTests
{
    [Fact]
    public void Bounds_are_utc_offset_zero_so_timestamptz_accepts_them()
    {
        var (start, end) = ClosingReportService.PeriodBoundsUtc(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));

        Assert.Equal(TimeSpan.Zero, start.Offset);
        Assert.Equal(TimeSpan.Zero, end.Offset);
    }

    [Fact]
    public void Bounds_cover_whole_business_days_and_end_is_exclusive()
    {
        var (start, end) = ClosingReportService.PeriodBoundsUtc(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));

        // Chile en octubre 2026 = UTC-3: el día 1 arranca 03:00 UTC y el 1 de noviembre
        // (exclusivo) arranca 03:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void Single_day_period_spans_exactly_24_hours()
    {
        var (start, end) = ClosingReportService.PeriodBoundsUtc(new DateTime(2026, 10, 6), new DateTime(2026, 10, 6));

        Assert.Equal(TimeSpan.FromHours(24), end - start);
    }

    [Fact]
    public void Ignores_time_of_day_on_the_input_dates()
    {
        var (a, _) = ClosingReportService.PeriodBoundsUtc(new DateTime(2026, 10, 1, 23, 59, 0), new DateTime(2026, 10, 1));
        var (b, _) = ClosingReportService.PeriodBoundsUtc(new DateTime(2026, 10, 1, 0, 0, 0), new DateTime(2026, 10, 1));

        Assert.Equal(a, b);
    }
}
