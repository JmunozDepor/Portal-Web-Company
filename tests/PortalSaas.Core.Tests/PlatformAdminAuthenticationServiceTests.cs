using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

public class PlatformAdminAuthenticationServiceTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(PortalSaasDbContext Db, PlatformAdmin Admin)> CrearAdminAsync(string password)
    {
        var db = CrearContexto();

        var (hash, salt) = PasswordHasher.Hash(password);
        var admin = new PlatformAdmin
        {
            Email = "ceo@comercialdepor.cl",
            PasswordHash = hash,
            PasswordSalt = salt,
        };
        db.PlatformAdmins.Add(admin);
        await db.SaveChangesAsync();

        return (db, admin);
    }

    [Fact]
    public async Task Authenticate_ConCredencialesCorrectas_Exito()
    {
        var (db, admin) = await CrearAdminAsync("Clave.Correcta1!");
        var servicio = new PlatformAdminAuthenticationService(db);

        var resultado = await servicio.AuthenticateAsync("CEO@comercialdepor.cl", "Clave.Correcta1!");

        Assert.True(resultado.IsSuccess);
        Assert.Equal(admin.Id, resultado.UserId);
    }

    [Fact]
    public async Task Authenticate_ContrasenaIncorrecta_Falla_YNoRevelaSiElCorreoExiste()
    {
        var (db, _) = await CrearAdminAsync("Clave.Correcta1!");
        var servicio = new PlatformAdminAuthenticationService(db);

        var resultadoCorreoReal = await servicio.AuthenticateAsync("ceo@comercialdepor.cl", "clave-incorrecta");
        var resultadoCorreoInexistente = await servicio.AuthenticateAsync("no-existe@comercialdepor.cl", "clave-incorrecta");

        Assert.False(resultadoCorreoReal.IsSuccess);
        Assert.False(resultadoCorreoInexistente.IsSuccess);
        Assert.Equal(resultadoCorreoReal.Reason, resultadoCorreoInexistente.Reason);
    }

    [Fact]
    public async Task Authenticate_IncrementaIntentosFallidos_YResetAlAcertar()
    {
        var (db, admin) = await CrearAdminAsync("Clave.Correcta1!");
        var servicio = new PlatformAdminAuthenticationService(db);

        await servicio.AuthenticateAsync("ceo@comercialdepor.cl", "mala1");
        await servicio.AuthenticateAsync("ceo@comercialdepor.cl", "mala2");

        var actualizado = await db.PlatformAdmins.FindAsync(admin.Id);
        Assert.Equal(2, actualizado!.FailedLoginAttempts);

        await servicio.AuthenticateAsync("ceo@comercialdepor.cl", "Clave.Correcta1!");

        var trasExito = await db.PlatformAdmins.FindAsync(admin.Id);
        Assert.Equal(0, trasExito!.FailedLoginAttempts);
        Assert.NotNull(trasExito.LastLoginAt);
    }

    [Fact]
    public async Task Authenticate_TrasAlcanzarElUmbral_BloqueaLaCuenta()
    {
        var (db, admin) = await CrearAdminAsync("Clave.Correcta1!");
        var servicio = new PlatformAdminAuthenticationService(db);

        for (var i = 0; i < PlatformAdminAuthenticationService.MaxFailedAttempts; i++)
        {
            await servicio.AuthenticateAsync("ceo@comercialdepor.cl", "clave-mala");
        }

        var actualizado = await db.PlatformAdmins.FindAsync(admin.Id);
        Assert.True(actualizado!.IsLocked);

        // Incluso con la contraseña correcta, una cuenta ya bloqueada se rechaza sin
        // evaluarla -- ver el contrato IPlatformAdminAuthenticationService.
        var resultado = await servicio.AuthenticateAsync("ceo@comercialdepor.cl", "Clave.Correcta1!");
        Assert.False(resultado.IsSuccess);
        Assert.Contains("bloqueada", resultado.Reason);
    }

    [Fact]
    public async Task Authenticate_CuentaInactiva_Rechaza()
    {
        var (db, admin) = await CrearAdminAsync("Clave.Correcta1!");
        admin.IsActive = false;
        await db.SaveChangesAsync();

        var servicio = new PlatformAdminAuthenticationService(db);
        var resultado = await servicio.AuthenticateAsync("ceo@comercialdepor.cl", "Clave.Correcta1!");

        Assert.False(resultado.IsSuccess);
        Assert.Contains("inactiva", resultado.Reason);
    }
}
