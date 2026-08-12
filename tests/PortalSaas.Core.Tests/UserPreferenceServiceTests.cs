using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Tests.TestHelpers;
using PortalSaas.Core.Usuarios;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

public class UserPreferenceServiceTests
{
    private static async Task<(PortalSaasDbContext Db, User User)> CrearUsuarioAsync()
    {
        var db = new PortalSaasDbContext(
            new DbContextOptionsBuilder<PortalSaasDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new NullOrganizationScopeProvider());

        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var user = new User { OrganizationId = org.Id, Username = "jperez", Email = "jperez@cliente.cl", PasswordHash = "x", PasswordSalt = "x" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (db, user);
    }

    [Fact]
    public async Task GetOrCreateDefault_SinPreferenciasPrevias_CreaConDefaults()
    {
        var (db, user) = await CrearUsuarioAsync();
        var servicio = new UserPreferenceService(db);

        var preferencias = await servicio.GetOrCreateDefaultAsync(user.Id);

        Assert.Equal("es-CL", preferencias.Locale);
        Assert.Equal(UserThemePreference.System, preferencias.Theme);
        Assert.True(preferencias.EmailNotificationsEnabled);
    }

    [Fact]
    public async Task GetOrCreateDefault_LlamadoDosVeces_NoDuplicaLaFila()
    {
        var (db, user) = await CrearUsuarioAsync();
        var servicio = new UserPreferenceService(db);

        await servicio.GetOrCreateDefaultAsync(user.Id);
        await servicio.GetOrCreateDefaultAsync(user.Id);

        Assert.Equal(1, await db.UserPreferences.CountAsync(p => p.UserId == user.Id));
    }

    [Fact]
    public async Task Update_CambiaLasPreferenciasGuardadas()
    {
        var (db, user) = await CrearUsuarioAsync();
        var servicio = new UserPreferenceService(db);
        await servicio.GetOrCreateDefaultAsync(user.Id);

        await servicio.UpdateAsync(user.Id, new UserPreferenceDto("en-US", "UTC", UserThemePreference.Dark, false));

        var actualizado = await servicio.GetOrCreateDefaultAsync(user.Id);
        Assert.Equal("en-US", actualizado.Locale);
        Assert.Equal(UserThemePreference.Dark, actualizado.Theme);
        Assert.False(actualizado.EmailNotificationsEnabled);
    }
}
