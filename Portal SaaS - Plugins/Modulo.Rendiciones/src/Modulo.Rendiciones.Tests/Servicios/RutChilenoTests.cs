using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

public class RutChilenoTests
{
    [Theory]
    [InlineData("76.192.083-9", "76192083-9")]
    [InlineData("76192083-9", "76192083-9")]
    [InlineData("  76.192.083 - 9 ", "76192083-9")]
    [InlineData("5.126.663-3", "5126663-3")]
    [InlineData("12.345.678-5", "12345678-5")]
    [InlineData("7.654.321-6", "7654321-6")]
    public void Normalizes_valid_ruts(string raw, string expected)
    {
        Assert.Equal(expected, RutChileno.NormalizeOrNull(raw));
    }

    [Theory]
    [InlineData("76.192.083-K")]  // DV incorrecto
    [InlineData("12.345.678-9")]  // DV incorrecto
    [InlineData("")]
    [InlineData(null)]
    [InlineData("no-es-un-rut")]
    [InlineData("1")]
    public void Rejects_invalid_ruts(string? raw)
    {
        Assert.Null(RutChileno.NormalizeOrNull(raw));
    }

    [Theory]
    [InlineData(76192083L, '9')]
    [InlineData(5126663L, '3')]
    [InlineData(11111111L, '1')]
    [InlineData(6L, 'K')]
    [InlineData(44444446L, '0')]
    public void Computes_check_digit(long body, char dv)
    {
        Assert.Equal(dv, RutChileno.CalcularDv(body));
    }

    [Fact]
    public void Accepts_K_check_digit_lowercase()
    {
        Assert.Equal("6-K", RutChileno.NormalizeOrNull("6-k"));
    }
}
