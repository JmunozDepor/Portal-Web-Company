using Modulo.Despachos.Dominio;

namespace Modulo.Despachos.Tests.Dominio;

/// <summary>Código visible de la agrupación (AT-000123), el mismo que lleva el Code128 de la hoja.</summary>
public class DispatchGroupCodeTests
{
    [Fact]
    public void Format_RellenaASeisDigitos() => Assert.Equal("AT-000123", DispatchGroupCode.Format(123));

    [Theory]
    [InlineData("AT-000123", 123)]
    [InlineData(" at-12 ", 12)]
    public void TryParse_Valido(string raw, int expected)
    {
        Assert.True(DispatchGroupCode.TryParse(raw, out var n));
        Assert.Equal(expected, n);
    }

    [Theory]
    [InlineData("AT-")]
    [InlineData("ATX-1")]
    [InlineData("CTBO123")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("AT-1234567890")]
    public void TryParse_Invalido(string? raw) => Assert.False(DispatchGroupCode.TryParse(raw, out _));
}
