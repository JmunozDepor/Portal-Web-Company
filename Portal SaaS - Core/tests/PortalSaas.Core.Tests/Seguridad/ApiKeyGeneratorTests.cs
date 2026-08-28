using PortalSaas.Core.Seguridad;
using Xunit;

namespace PortalSaas.Core.Tests.Seguridad;

public class ApiKeyGeneratorTests
{
    [Fact]
    public void GenerateRawKey_GeneraValoresDistintosCadaVez()
    {
        var primeraKey = ApiKeyGenerator.GenerateRawKey();
        var segundaKey = ApiKeyGenerator.GenerateRawKey();

        Assert.NotEqual(primeraKey, segundaKey);
    }

    [Fact]
    public void Hash_EsDeterministaParaElMismoValor()
    {
        var hash1 = ApiKeyGenerator.Hash("mi-clave-de-prueba");
        var hash2 = ApiKeyGenerator.Hash("mi-clave-de-prueba");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_EsDistintoParaValoresDistintos()
    {
        var hashA = ApiKeyGenerator.Hash("clave-a");
        var hashB = ApiKeyGenerator.Hash("clave-b");

        Assert.NotEqual(hashA, hashB);
    }

    [Fact]
    public void Hash_NuncaContieneLaClaveOriginal()
    {
        var claveOriginal = "clave-super-secreta";
        var hash = ApiKeyGenerator.Hash(claveOriginal);

        Assert.DoesNotContain(claveOriginal, hash);
    }
}
