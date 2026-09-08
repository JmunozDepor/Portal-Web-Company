using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Seguridad;

public class UserSessionServiceTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(PortalSaasDbContext Db, Organization Org, User User)> CrearUsuarioAsync()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var user = new User
        {
            OrganizationId = org.Id,
            Username = "jperez",
            Email = "jperez@cliente.cl",
            PasswordHash = "x",
            PasswordSalt = "x",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (db, org, user);
    }

    private static void AgregarSesion(PortalSaasDbContext db, Organization org, User user, DateTimeOffset createdAt, bool revocada = false)
    {
        db.UserSessions.Add(new UserSession
        {
            UserId = user.Id,
            OrganizationId = org.Id,
            TokenHash = Guid.NewGuid().ToString(),
            CreatedAt = createdAt,
            IsRevoked = revocada,
        });
    }

    [Fact]
    public async Task ListActiveAsync_omite_sesiones_mas_viejas_que_la_vida_de_la_cookie()
    {
        var (db, org, user) = await CrearUsuarioAsync();
        AgregarSesion(db, org, user, DateTimeOffset.UtcNow.AddMinutes(-30));            // reciente
        AgregarSesion(db, org, user, DateTimeOffset.UtcNow.AddHours(-7));               // dentro de la ventana de 8h
        AgregarSesion(db, org, user, DateTimeOffset.UtcNow.AddHours(-9));               // vieja -> "historia"
        AgregarSesion(db, org, user, DateTimeOffset.UtcNow.AddDays(-40));               // vieja -> "historia"
        await db.SaveChangesAsync();

        var activas = await new UserSessionService(db).ListActiveAsync();

        Assert.Equal(2, activas.Count);
    }

    [Fact]
    public async Task ListActiveAsync_sigue_omitiendo_las_revocadas_aunque_sean_recientes()
    {
        var (db, org, user) = await CrearUsuarioAsync();
        AgregarSesion(db, org, user, DateTimeOffset.UtcNow.AddMinutes(-5));
        AgregarSesion(db, org, user, DateTimeOffset.UtcNow.AddMinutes(-5), revocada: true);
        await db.SaveChangesAsync();

        var activas = await new UserSessionService(db).ListActiveAsync();

        Assert.Single(activas);
    }
}
