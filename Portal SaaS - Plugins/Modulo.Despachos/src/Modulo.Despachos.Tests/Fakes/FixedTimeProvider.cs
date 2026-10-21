namespace Modulo.Despachos.Tests.Fakes;

/// <summary>Reloj fijo para pruebas (UTC).</summary>
public sealed class FixedTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; }
    public FixedTimeProvider(DateTimeOffset now) => Now = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
