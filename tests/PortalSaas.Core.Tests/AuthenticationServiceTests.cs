using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

public class AuthenticationServiceTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options, new NullOrganizationScopeProvider());

    private static async Task<(PortalSaasDbContext Db, Organization Org, User User)> CrearUsuarioAsync(string password)
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var (hash, salt) = PasswordHasher.Hash(password);
        var user = new User
        {
            OrganizationId = org.Id,
            Username = "jperez",
            Email = "jperez@cliente.cl",
            PasswordHash = hash,
            PasswordSalt = salt,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (db, org, user);
    }

    [Fact]
    public async Task Authenticate_ConCredencialesCorrectas_PorUsername_Exito()
    {
        var (db, org, user) = await CrearUsuarioAsync("Clave.Correcta1!");
        var servicio = new AuthenticationService(db);

        var resultado = await servicio.AuthenticateAsync(org.Id, "jperez", "Clave.Correcta1!");

        Assert.True(resultado.IsSuccess);
        Assert.Equal(user.Id, resultado.UserId);
    }

    [Fact]
    public async Task Authenticate_ConCredencialesCorrectas_PorEmail_Exito()
    {
        var (db, org, _) = await CrearUsuarioAsync("Clave.Correcta1!");
        var servicio = new AuthenticationService(db);

        // Debe poder loguearse con el correo, no solo con el nombre de usuario --
        // pedido explícito del usuario: la cuenta siempre asociada a un correo.
        var resultado = await servicio.AuthenticateAsync(org.Id, "JPEREZ@cliente.cl", "Clave.Correcta1!");

        Assert.True(resultado.IsSuccess);
    }

    [Fact]
    public async Task Authenticate_ContrasenaIncorrecta_Falla_YNoRevelaSiElUsuarioExiste()
    {
        var (db, org, _) = await CrearUsuarioAsync("Clave.Correcta1!");
        var servicio = new AuthenticationService(db);

        var resultadoUsuarioReal = await servicio.AuthenticateAsync(org.Id, "jperez", "clave-incorrecta");
        var resultadoUsuarioInexistente = await servicio.AuthenticateAsync(org.Id, "no-existe", "clave-incorrecta");

        Assert.False(resultadoUsuarioReal.IsSuccess);
        Assert.False(resultadoUsuarioInexistente.IsSuccess);
        Assert.Equal(resultadoUsuarioReal.Reason, resultadoUsuarioInexistente.Reason);
    }

    [Fact]
    public async Task Authenticate_IncrementaIntentosFallidos_YResetAlAcertar()
    {
        var (db, org, user) = await CrearUsuarioAsync("Clave.Correcta1!");
        var servicio = new AuthenticationService(db);

        await servicio.AuthenticateAsync(org.Id, "jperez", "mala1");
        await servicio.AuthenticateAsync(org.Id, "jperez", "mala2");

        var actualizado = await db.Users.FindAsync(user.Id);
        Assert.Equal(2, actualizado!.FailedLoginAttempts);

        await servicio.AuthenticateAsync(org.Id, "jperez", "Clave.Correcta1!");

        var trasExito = await db.Users.FindAsync(user.Id);
        Assert.Equal(0, trasExito!.FailedLoginAttempts);
        Assert.NotNull(trasExito.LastLoginAt);
    }

    [Fact]
    public async Task Authenticate_TrasAlcanzarElUmbral_BloqueaLaCuenta()
    {
        var (db, org, user) = await CrearUsuarioAsync("Clave.Correcta1!");
        var servicio = new AuthenticationService(db);

        for (var i = 0; i < AuthenticationService.MaxFailedAttempts; i++)
        {
            await servicio.AuthenticateAsync(org.Id, "jperez", "clave-mala");
        }

        var actualizado = await db.Users.FindAsync(user.Id);
        Assert.True(actualizado!.IsLocked);

        // Incluso con la contraseña correcta, una cuenta ya bloqueada se rechaza sin
        // evaluarla -- ver el contrato IAuthenticationService.
        var resultado = await servicio.AuthenticateAsync(org.Id, "jperez", "Clave.Correcta1!");
        Assert.False(resultado.IsSuccess);
        Assert.Contains("bloqueada", resultado.Reason);
    }

    [Fact]
    public async Task Authenticate_CuentaInactiva_Rechaza()
    {
        var (db, org, user) = await CrearUsuarioAsync("Clave.Correcta1!");
        user.IsActive = false;
        await db.SaveChangesAsync();

        var servicio = new AuthenticationService(db);
        var resultado = await servicio.AuthenticateAsync(org.Id, "jperez", "Clave.Correcta1!");

        Assert.False(resultado.IsSuccess);
        Assert.Contains("inactiva", resultado.Reason);
    }
}
