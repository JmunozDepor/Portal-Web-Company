using Modulo.Despachos.Dominio;

namespace Modulo.Despachos.Tests.Dominio;

public class PlateNormalizerTests
{
    [Fact]
    public void Normalize_QuitaSeparadoresYMayusculas()
    {
        Assert.Equal("ABCD12", PlateNormalizer.Normalize(" ab-cd 12 "));
        Assert.Equal("AB1234", PlateNormalizer.Normalize("ab.12.34"));
    }
}
