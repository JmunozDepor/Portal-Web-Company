using PortalSaas.Core.Seguridad;
using Xunit;

namespace PortalSaas.Core.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_LuegoVerify_ConLaMismaContrasena_DevuelveTrue()
    {
        var (hash, salt) = PasswordHasher.Hash("Mi.Contraseña.Real!");

        Assert.True(PasswordHasher.Verify("Mi.Contraseña.Real!", hash, salt));
    }

    [Fact]
    public void Verify_ConContrasenaIncorrecta_DevuelveFalse()
    {
        var (hash, salt) = PasswordHasher.Hash("Mi.Contraseña.Real!");

        Assert.False(PasswordHasher.Verify("otra-contraseña", hash, salt));
    }

    [Fact]
    public void Hash_DosVecesLaMismaContrasena_ProduceSaltsYHashesDistintos()
    {
        var (hash1, salt1) = PasswordHasher.Hash("misma-contraseña");
        var (hash2, salt2) = PasswordHasher.Hash("misma-contraseña");

        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
    }
}
