using Modulo.Despachos.Dominio;

namespace Modulo.Despachos.Tests.Dominio;

/// <summary>Código del borrador de guía manual (GM-000123), el que se escanea o digita en el manifiesto.</summary>
public class ManualGuideCodeTests
{
    [Fact]
    public void Format_RellenaASeisDigitos() => Assert.Equal("GM-000123", ManualGuideCode.Format(123));

    [Theory]
    [InlineData("GM-000123", 123)]
    [InlineData(" gm-000123 ", 123)]
    public void TryParse_Valido(string raw, int expected)
    {
        Assert.True(ManualGuideCode.TryParse(raw, out var n));
        Assert.Equal(expected, n);
    }

    [Theory]
    [InlineData("AT-000123")]
    [InlineData("GM-")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_Invalido(string? raw) => Assert.False(ManualGuideCode.TryParse(raw, out _));
}
