using Microsoft.Extensions.Configuration;
using PortalSaas.Core.Seguridad;
using Xunit;

namespace PortalSaas.Core.Tests;

public class SecretoCifradoServiceTests
{
    private static SecretoCifradoService CrearServicio()
    {
        // Clave AES-256 (32 bytes) fija, SOLO para tests -- nunca usar en ningún ambiente real.
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:MasterSecretKey"] = Convert.ToBase64String(new byte[32]),
            })
            .Build();

        return new SecretoCifradoService(configuracion);
    }

    [Fact]
    public void Encrypt_LuegoDecrypt_DevuelveElTextoOriginal()
    {
        var servicio = CrearServicio();
        var original = "Contraseña.Real.2026!";

        var cifrado = servicio.Encrypt(original);
        var descifrado = servicio.Decrypt(cifrado);

        Assert.Equal(original, descifrado);
    }

    [Fact]
    public void Encrypt_DosVecesElMismoTexto_ProduceCifradosDistintos()
    {
        // El nonce aleatorio por operación (ver comentario en SecretoCifradoService)
        // garantiza esto -- si algún día se cifra el mismo valor dos veces igual, es
        // una regresión real de seguridad, no un detalle cosmético.
        var servicio = CrearServicio();

        var cifrado1 = servicio.Encrypt("mismo-valor");
        var cifrado2 = servicio.Encrypt("mismo-valor");

        Assert.NotEqual(cifrado1, cifrado2);
    }

    [Fact]
    public void Constructor_SinClaveMaestraConfigurada_LanzaExcepcion()
    {
        var configuracionVacia = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => new SecretoCifradoService(configuracionVacia));
    }

    [Fact]
    public void Constructor_ClaveDeTamanoIncorrecto_LanzaExcepcion()
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:MasterSecretKey"] = Convert.ToBase64String(new byte[16]), // AES-128, no AES-256
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new SecretoCifradoService(configuracion));
    }
}
