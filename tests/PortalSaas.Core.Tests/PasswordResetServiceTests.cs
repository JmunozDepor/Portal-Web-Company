using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

public class PasswordResetServiceTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options, new NullOrganizationScopeProvider());

    private static async Task<(PortalSaasDbContext Db, Organization Org, User User)> CrearUsuarioAsync()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var (hash, salt) = PasswordHasher.Hash("Clave.Vieja1!");
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
    public async Task RequestReset_ConCorreoExistente_DevuelveUnToken()
    {
        var (db, org, _) = await CrearUsuarioAsync();
        var servicio = new PasswordResetService(db);

        var token = await servicio.RequestResetAsync(org.Id, "JPEREZ@cliente.cl", default);

        Assert.NotNull(token);
    }

    [Fact]
    public async Task RequestReset_ConCorreoInexistente_DevuelveNull()
    {
        var (db, org, _) = await CrearUsuarioAsync();
        var servicio = new PasswordResetService(db);

        var token = await servicio.RequestResetAsync(org.Id, "no-existe@cliente.cl", default);

        Assert.Null(token);
    }

    [Fact]
    public async Task ResetPassword_ConTokenValido_CambiaLaContrasenaYLaMarcaUsado()
    {
        var (db, org, user) = await CrearUsuarioAsync();
        var servicio = new PasswordResetService(db);
        var token = await servicio.RequestResetAsync(org.Id, "jperez@cliente.cl", default);

        var exito = await servicio.ResetPasswordAsync(token!, "Clave.Nueva2!");

        Assert.True(exito);
        var actualizado = await db.Users.FindAsync(user.Id);
        Assert.True(PasswordHasher.Verify("Clave.Nueva2!", actualizado!.PasswordHash, actualizado.PasswordSalt));
    }

    [Fact]
    public async Task ResetPassword_ConElMismoTokenDosVeces_LaSegundaVezFalla()
    {
        var (db, org, _) = await CrearUsuarioAsync();
        var servicio = new PasswordResetService(db);
        var token = await servicio.RequestResetAsync(org.Id, "jperez@cliente.cl", default);

        var primerUso = await servicio.ResetPasswordAsync(token!, "Clave.Nueva2!");
        var segundoUso = await servicio.ResetPasswordAsync(token!, "Otra.Clave3!");

        Assert.True(primerUso);
        Assert.False(segundoUso);
    }

    [Fact]
    public async Task ResetPassword_TokenExpirado_Falla()
    {
        var (db, org, user) = await CrearUsuarioAsync();
        var servicio = new PasswordResetService(db);
        var token = await servicio.RequestResetAsync(org.Id, "jperez@cliente.cl", default);

        // Forzar expiración manualmente -- no depender de un Thread.Sleep de 1 hora real.
        var registro = await db.PasswordResetTokens.FirstAsync(t => t.UserId == user.Id);
        registro.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var exito = await servicio.ResetPasswordAsync(token!, "Clave.Nueva2!");

        Assert.False(exito);
    }

    [Fact]
    public async Task ResetPassword_TokenInventado_Falla()
    {
        var (db, _, _) = await CrearUsuarioAsync();
        var servicio = new PasswordResetService(db);

        var exito = await servicio.ResetPasswordAsync(Convert.ToBase64String(new byte[32]), "Clave.Nueva2!");

        Assert.False(exito);
    }
}
