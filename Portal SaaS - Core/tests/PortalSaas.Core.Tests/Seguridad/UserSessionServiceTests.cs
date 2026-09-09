using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Modelos;
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

    // lastSeenAt representa la última actividad de la sesión -- la cookie usa
    // SlidingExpiration, así que es contra este valor (no CreatedAt) que se mide si
    // sigue "conectada".
    private static void AgregarSesion(PortalSaasDbContext db, Organization org, User user, DateTimeOffset lastSeenAt, bool revocada = false)
    {
        db.UserSessions.Add(new UserSession
        {
            UserId = user.Id,
            OrganizationId = org.Id,
            TokenHash = Guid.NewGuid().ToString(),
            CreatedAt = lastSeenAt,
            LastSeenAt = lastSeenAt,
            IsRevoked = revocada,
        });
    }

    [Fact]
    public async Task ListActiveAsync_omite_sesiones_sin_actividad_hace_mas_que_la_vida_de_la_cookie()
    {
        var (db, org, user) = await CrearUsuarioAsync();
        AgregarSesion(db, org, user, DateTimeOffset.UtcNow.AddMinutes(-30));            // reciente
        AgregarSesion(db, org, user, DateTimeOffset.UtcNow.AddHours(-7));               // dentro de la ventana de 8h
        AgregarSesion(db, org, user, DateTimeOffset.UtcNow.AddHours(-9));               // sin actividad -> "historia"
        AgregarSesion(db, org, user, DateTimeOffset.UtcNow.AddDays(-40));               // sin actividad -> "historia"
        await db.SaveChangesAsync();

        var activas = await new UserSessionService(db).ListActiveAsync();

        Assert.Equal(2, activas.Count);
    }

    [Fact]
    public async Task ListActiveAsync_cuenta_una_sesion_vieja_pero_con_actividad_reciente()
    {
        // El bug que este fix corrige: sesión logueada hace 10 h pero activa hace 2 min
        // -- con SlidingExpiration la cookie sigue viva, así que debe seguir "conectada".
        var (db, org, user) = await CrearUsuarioAsync();
        db.UserSessions.Add(new UserSession
        {
            UserId = user.Id,
            OrganizationId = org.Id,
            TokenHash = Guid.NewGuid().ToString(),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-10),
            LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-2),
        });
        await db.SaveChangesAsync();

        var activas = await new UserSessionService(db).ListActiveAsync();

        Assert.Single(activas);
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

    [Fact]
    public async Task ValidateAndTouchAsync_sesion_normal_devuelve_Active()
    {
        var (db, org, user) = await CrearUsuarioAsync();
        var service = new UserSessionService(db);
        var token = await service.CreateAsync(user.Id, org.Id, null, null);

        Assert.Equal(SessionValidationStatus.Active, await service.ValidateAndTouchAsync(token));
    }

    [Fact]
    public async Task ValidateAndTouchAsync_sesion_revocada_devuelve_Revoked()
    {
        var (db, org, user) = await CrearUsuarioAsync();
        var service = new UserSessionService(db);
        var token = await service.CreateAsync(user.Id, org.Id, null, null);
        await service.RevokeByTokenAsync(token);

        Assert.Equal(SessionValidationStatus.Revoked, await service.ValidateAndTouchAsync(token));
    }

    [Fact]
    public async Task ValidateAndTouchAsync_token_sin_fila_devuelve_NotFound_no_Revoked()
    {
        // Clave del fix: una fila ausente (purgada / base recreada) NO debe cerrar la
        // cookie -- por eso es NotFound y no Revoked.
        var (db, org, user) = await CrearUsuarioAsync();
        var service = new UserSessionService(db);
        var token = await service.CreateAsync(user.Id, org.Id, null, null);

        db.UserSessions.RemoveRange(db.UserSessions);
        await db.SaveChangesAsync();

        Assert.Equal(SessionValidationStatus.NotFound, await service.ValidateAndTouchAsync(token));
    }

    [Fact]
    public async Task ValidateAndTouchAsync_token_no_base64_devuelve_NotFound_sin_excepcion()
    {
        var (db, _, _) = await CrearUsuarioAsync();
        var service = new UserSessionService(db);

        Assert.Equal(SessionValidationStatus.NotFound, await service.ValidateAndTouchAsync("no-es-base64!!"));
    }

    [Fact]
    public async Task ValidateAndTouchAsync_refresca_LastSeenAt_cuando_paso_el_umbral()
    {
        var (db, org, user) = await CrearUsuarioAsync();
        var service = new UserSessionService(db);
        var token = await service.CreateAsync(user.Id, org.Id, null, null);

        var sesion = await db.UserSessions.SingleAsync();
        sesion.LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        await db.SaveChangesAsync();

        await service.ValidateAndTouchAsync(token);

        var refrescada = await db.UserSessions.AsNoTracking().SingleAsync();
        Assert.True(refrescada.LastSeenAt >= DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task ValidateAndTouchAsync_no_reescribe_LastSeenAt_si_fue_hace_poco()
    {
        var (db, org, user) = await CrearUsuarioAsync();
        var service = new UserSessionService(db);
        var token = await service.CreateAsync(user.Id, org.Id, null, null);

        var marca = DateTimeOffset.UtcNow.AddMinutes(-1);
        var sesion = await db.UserSessions.SingleAsync();
        sesion.LastSeenAt = marca;
        await db.SaveChangesAsync();

        await service.ValidateAndTouchAsync(token);

        var despues = await db.UserSessions.AsNoTracking().SingleAsync();
        Assert.Equal(marca, despues.LastSeenAt);
    }
}
