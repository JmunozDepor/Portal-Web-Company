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

    [Fact]
    public void Format_Entrega() => Assert.Equal("EG-000123", DispatchGroupCode.Format(123, "delivery"));

    [Theory]
    [InlineData("EG-000123", 123, "delivery")]
    [InlineData("at-7", 7, "transfer")]
    public void TryParse_ConTipo(string raw, int number, string kind)
    {
        Assert.True(DispatchGroupCode.TryParse(raw, out var n, out var k));
        Assert.Equal((number, kind), (n, k));
    }

    [Fact]
    public void TryParse_ConTipo_Invalido() => Assert.False(DispatchGroupCode.TryParse("EG-", out _, out _));

    [Fact]
    public void TryParse_SinTipo_SoloAT() => Assert.False(DispatchGroupCode.TryParse("EG-000123", out _));
}
