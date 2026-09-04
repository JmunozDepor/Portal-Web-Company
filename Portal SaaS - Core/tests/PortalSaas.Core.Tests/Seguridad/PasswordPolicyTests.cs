using PortalSaas.Core.Seguridad;
using Xunit;

namespace PortalSaas.Core.Tests.Seguridad;

public class PasswordPolicyTests
{
    [Fact]
    public void Validate_ContrasenaQueCumpleTodasLasReglas_NoDevuelveErrores()
    {
        var errores = PasswordPolicy.Validate("Abcdef12!$");

        Assert.Empty(errores);
    }

    [Fact]
    public void Validate_MenosDeDiezCaracteres_DevuelveErrorDeLongitud()
    {
        var errores = PasswordPolicy.Validate("Ab1!Ab1!");

        Assert.Contains(errores, e => e.Contains("10 caracteres"));
    }

    [Fact]
    public void Validate_SinMayuscula_DevuelveErrorCorrespondiente()
    {
        var errores = PasswordPolicy.Validate("abcdefg12!$");

        Assert.Contains(errores, e => e.Contains("mayúscula"));
    }

    [Fact]
    public void Validate_SinMinuscula_DevuelveErrorCorrespondiente()
    {
        var errores = PasswordPolicy.Validate("ABCDEFG12!$");

        Assert.Contains(errores, e => e.Contains("minúscula"));
    }

    [Fact]
    public void Validate_SinDigito_DevuelveErrorCorrespondiente()
    {
        var errores = PasswordPolicy.Validate("Abcdefghi!$");

        Assert.Contains(errores, e => e.Contains("número"));
    }

    [Fact]
    public void Validate_SinSimbolo_DevuelveErrorCorrespondiente()
    {
        var errores = PasswordPolicy.Validate("Abcdefghi12");

        Assert.Contains(errores, e => e.Contains("símbolo"));
    }

    [Fact]
    public void Validate_ContrasenaVacia_DevuelveTodosLosErroresAplicables()
    {
        var errores = PasswordPolicy.Validate("");

        Assert.Equal(5, errores.Count);
    }
}
