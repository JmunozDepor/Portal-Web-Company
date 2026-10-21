using Modulo.Despachos.Dominio;

namespace Modulo.Despachos.Tests.Dominio;

public class ChileClockTests
{
    [Fact]
    public void ToLocal_UsaHorarioDeVeranoChile()
    {
        // 2026-11-10 20:42 UTC = 17:42 Chile (UTC-3, horario de verano)
        Assert.Equal(new TimeOnly(17, 42),
            TimeOnly.FromDateTime(ChileClock.ToLocal(new DateTime(2026, 11, 10, 20, 42, 0, DateTimeKind.Utc))));
    }

    [Fact]
    public void ToUtc_DevuelveKindUtcYRoundTrip()
    {
        var utc = ChileClock.ToUtc(new DateTime(2026, 11, 10, 17, 42, 0));
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 11, 10, 20, 42, 0), utc);
    }
}
