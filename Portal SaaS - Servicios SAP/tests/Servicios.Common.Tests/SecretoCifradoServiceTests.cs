using Microsoft.Extensions.Configuration;
using Servicios.Common.Seguridad;
using Xunit;

namespace Servicios.Common.Tests;

public class SecretoCifradoServiceTests
{
    private static SecretoCifradoService CrearServicio()
    {
        var claveBase64 = Convert.ToBase64String(new byte[32]);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seguridad:ClaveMaestraSecretos"] = claveBase64
            })
            .Build();

        return new SecretoCifradoService(config);
    }

    [Fact]
    public void Cifrar_Descifrar_RoundTrip_DevuelveElMismoTexto()
    {
        var servicio = CrearServicio();

        var cifrado = servicio.Cifrar("Inxap.2023");

        Assert.Equal("Inxap.2023", servicio.Descifrar(cifrado));
    }

    [Fact]
    public void Cifrar_MismoTextoDosVeces_ProduceCifradosDistintos()
    {
        var servicio = CrearServicio();

        var primero = servicio.Cifrar("mismo-secreto");
        var segundo = servicio.Cifrar("mismo-secreto");

        Assert.NotEqual(primero, segundo);
    }

    [Fact]
    public void Constructor_SinClaveMaestraConfigurada_Lanza()
    {
        var config = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => new SecretoCifradoService(config));
    }
}
