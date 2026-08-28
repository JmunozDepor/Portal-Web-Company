using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Tests.Infraestructura;

public class UserContactLookupServiceTests
{
    private static PortalSaasDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PortalSaasDbContext(options);
    }

    [Fact]
    public async Task GetContactAsync_devuelve_email_organizacion_y_preferencia_de_notificacion()
    {
        await using var db = CreateDb(nameof(GetContactAsync_devuelve_email_organizacion_y_preferencia_de_notificacion));

        var org = new Organization { Id = Guid.NewGuid(), LegalName = "Org Test", Slug = "org-test", Country = "CL" };
        var user = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Username = "aprobador1",
            Email = "aprobador1@test.cl",
            PasswordHash = "x",
            PasswordSalt = "x",
        };
        db.Organizations.Add(org);
        db.Users.Add(user);
        db.UserPreferences.Add(new UserPreference { UserId = user.Id, EmailNotificationsEnabled = false });
        await db.SaveChangesAsync();

        var sut = new UserContactLookupService(db);

        var result = await sut.GetContactAsync(user.Id);

        Assert.NotNull(result);
        Assert.Equal(org.Id, result!.OrganizationId);
        Assert.Equal("aprobador1@test.cl", result.Email);
        Assert.False(result.EmailNotificationsEnabled);
    }

    [Fact]
    public async Task GetContactAsync_sin_fila_de_preferencia_asume_notificaciones_habilitadas()
    {
        await using var db = CreateDb(nameof(GetContactAsync_sin_fila_de_preferencia_asume_notificaciones_habilitadas));

        var org = new Organization { Id = Guid.NewGuid(), LegalName = "Org Test", Slug = "org-test", Country = "CL" };
        var user = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Username = "aprobador2",
            Email = "aprobador2@test.cl",
            PasswordHash = "x",
            PasswordSalt = "x",
        };
        db.Organizations.Add(org);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = new UserContactLookupService(db);

        var result = await sut.GetContactAsync(user.Id);

        Assert.NotNull(result);
        Assert.True(result!.EmailNotificationsEnabled);
    }

    [Fact]
    public async Task GetContactAsync_usuario_inexistente_devuelve_null()
    {
        await using var db = CreateDb(nameof(GetContactAsync_usuario_inexistente_devuelve_null));
        var sut = new UserContactLookupService(db);

        var result = await sut.GetContactAsync(Guid.NewGuid());

        Assert.Null(result);
    }
}
