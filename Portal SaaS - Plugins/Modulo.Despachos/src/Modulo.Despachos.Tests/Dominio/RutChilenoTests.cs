using Modulo.Despachos.Dominio;

namespace Modulo.Despachos.Tests.Dominio;

public class RutChilenoTests
{
    [Fact]
    public void IsValid_ValidaDigitoVerificador()
    {
        Assert.True(RutChileno.IsValid("76.086.428-5"));
        Assert.False(RutChileno.IsValid("76086428-4"));
        Assert.False(RutChileno.IsValid(null));
        Assert.False(RutChileno.IsValid("abc"));
    }

    [Fact]
    public void Normalize_DejaFormatoCanonico()
    {
        Assert.Equal("12345678-5", RutChileno.Normalize("12.345.678-5"));
        Assert.Equal("7-K", RutChileno.Normalize("7k"));
    }
}
