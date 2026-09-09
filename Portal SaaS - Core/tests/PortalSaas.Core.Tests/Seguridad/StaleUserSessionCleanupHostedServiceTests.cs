using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Seguridad;

public class StaleUserSessionCleanupHostedServiceTests
{
    private static (ServiceProvider Sp, string DbName) CrearProveedor()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<PortalSaasDbContext>(o => o.UseInMemoryDatabase(dbName));
        return (services.BuildServiceProvider(), dbName);
    }

    private static async Task<(Guid OrgId, Guid UserId)> SembrarUsuarioAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var org = new Organization { LegalName = "Cliente", Slug = "cliente", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        var user = new User { OrganizationId = org.Id, Username = "u", Email = "u@c.cl", PasswordHash = "x", PasswordSalt = "x" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (org.Id, user.Id);
    }

    // lastSeenAt = última actividad de la sesión (ver UserSession.LastSeenAt) -- es
    // contra este valor, no CreatedAt, que la purga decide si está muerta.
    private static void AgregarSesion(PortalSaasDbContext db, Guid orgId, Guid userId, DateTimeOffset lastSeenAt, bool revocada = false) =>
        db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            OrganizationId = orgId,
            TokenHash = Guid.NewGuid().ToString(),
            CreatedAt = lastSeenAt,
            LastSeenAt = lastSeenAt,
            IsRevoked = revocada,
        });

    [Fact]
    public async Task PurgeOnceAsync_borra_vencidas_y_revocadas_y_deja_las_vigentes()
    {
        var (sp, _) = CrearProveedor();
        var (orgId, userId) = await SembrarUsuarioAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
            AgregarSesion(db, orgId, userId, DateTimeOffset.UtcNow.AddMinutes(-10));                 // vigente
            AgregarSesion(db, orgId, userId, DateTimeOffset.UtcNow.AddMinutes(-10), revocada: true); // revocada -> borrar
            AgregarSesion(db, orgId, userId, DateTimeOffset.UtcNow.AddHours(-9));                    // sin actividad -> borrar
            AgregarSesion(db, orgId, userId, DateTimeOffset.UtcNow.AddDays(-30));                    // sin actividad -> borrar

            // Logueada hace 10 h pero activa hace 5 min: con SlidingExpiration la cookie
            // sigue viva -- NO se borra (era el bug: se medía contra CreatedAt).
            db.UserSessions.Add(new UserSession
            {
                UserId = userId,
                OrganizationId = orgId,
                TokenHash = Guid.NewGuid().ToString(),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-10),
                LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            });
            await db.SaveChangesAsync();
        }

        var service = new StaleUserSessionCleanupHostedService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StaleUserSessionCleanupHostedService>.Instance);

        var borradas = await service.PurgeOnceAsync(default);

        Assert.Equal(3, borradas);

        using var verify = sp.CreateScope();
        var db2 = verify.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var restantes = await db2.UserSessions.ToListAsync();
        Assert.Equal(2, restantes.Count);
        Assert.All(restantes, s => Assert.False(s.IsRevoked));
    }

    [Fact]
    public async Task PurgeOnceAsync_sin_filas_para_borrar_devuelve_cero()
    {
        var (sp, _) = CrearProveedor();
        var (orgId, userId) = await SembrarUsuarioAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
            AgregarSesion(db, orgId, userId, DateTimeOffset.UtcNow.AddMinutes(-1));
            await db.SaveChangesAsync();
        }

        var service = new StaleUserSessionCleanupHostedService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StaleUserSessionCleanupHostedService>.Instance);

        Assert.Equal(0, await service.PurgeOnceAsync(default));
    }
}
