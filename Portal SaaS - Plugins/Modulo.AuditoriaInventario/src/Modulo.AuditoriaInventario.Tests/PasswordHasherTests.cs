using Modulo.AuditoriaInventario.Servicios;

namespace Modulo.AuditoriaInventario.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Verify_ConLaMismaContrasena_DevuelveTrue()
    {
        var (hash, salt) = PasswordHasher.Hash("Clave-Segura-123");

        Assert.True(PasswordHasher.Verify("Clave-Segura-123", hash, salt));
    }

    [Fact]
    public void Verify_ConContrasenaDistinta_DevuelveFalse()
    {
        var (hash, salt) = PasswordHasher.Hash("Clave-Segura-123");

        Assert.False(PasswordHasher.Verify("otra-clave", hash, salt));
    }

    [Fact]
    public void Hash_ConLaMismaContrasenaDosVeces_GeneraSalesYHashesDistintos()
    {
        var (hash1, salt1) = PasswordHasher.Hash("Clave-Segura-123");
        var (hash2, salt2) = PasswordHasher.Hash("Clave-Segura-123");

        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
    }
}
