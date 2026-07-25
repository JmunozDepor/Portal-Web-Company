using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

file sealed class CompanyAccessorFijo : ICurrentCompanyAccessor
{
    public required Guid CompanyId { get; init; }
    public string Code => "TEST";
    public string Database => "TESTDB";
    public string ServiceLayerUrl => "https://test.local/b1s/v1";
    public string Country => "CL";
    public bool HasCompany => true;
}

public class CurrentUserContextTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CurrentUserContext CrearServicio(PortalSaasDbContext db, Guid userId, Guid companyId, bool isAdmin)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, "usuario.prueba"),
                new Claim("IsAdmin", isAdmin.ToString()),
            ])),
        };
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
        var companyAccessor = new CompanyAccessorFijo { CompanyId = companyId };

        return new CurrentUserContext(httpContextAccessor, companyAccessor, db);
    }

    [Fact]
    public void UserId_Username_LeenSusClaims()
    {
        var db = CrearContexto();
        var userId = Guid.NewGuid();
        var servicio = CrearServicio(db, userId, Guid.NewGuid(), isAdmin: false);

        Assert.Equal(userId, servicio.UserId);
        Assert.Equal("usuario.prueba", servicio.Username);
    }

    [Fact]
    public async Task HasActionAsync_Admin_SiempreTrueSinConsultarNada()
    {
        var db = CrearContexto();
        var servicio = CrearServicio(db, Guid.NewGuid(), Guid.NewGuid(), isAdmin: true);

        var resultado = await servicio.HasActionAsync("Ventas.Ordenes", "VIEW");

        Assert.True(resultado);
    }

    [Fact]
    public async Task HasActionAsync_SinFilaDeAcceso_Deniega()
    {
        var db = CrearContexto();
        var servicio = CrearServicio(db, Guid.NewGuid(), Guid.NewGuid(), isAdmin: false);

        var resultado = await servicio.HasActionAsync("Ventas.Ordenes", "VIEW");

        Assert.False(resultado);
    }

    [Fact]
    public async Task HasActionAsync_ConPerfilQueIncluyeLaAccion_Permite()
    {
        var db = CrearContexto();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        var menu = new Menu { OriginModule = "Ventas", Code = "Ordenes", Name = "Órdenes", PagePath = "/ventas/ordenes" };
        var action = new PermissionAction { Code = "VIEW", Name = "Ver" };
        var profile = new Profile { Name = "Vendedor" };
        db.Menus.Add(menu);
        db.Actions.Add(action);
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();

        db.ProfileActions.Add(new ProfileAction { ProfileId = profile.Id, ActionId = action.Id });
        db.UserMenuProfiles.Add(new UserMenuProfile { UserId = userId, MenuId = menu.Id, ProfileId = profile.Id, CompanyId = companyId });
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db, userId, companyId, isAdmin: false);

        Assert.True(await servicio.HasActionAsync("Ventas.Ordenes", "VIEW"));
        Assert.False(await servicio.HasActionAsync("Ventas.Ordenes", "DELETE"));
    }

    [Fact]
    public async Task HasActionAsync_MismoUsuarioOtraCompania_NoHereda()
    {
        // UserMenuProfile es siempre por (UserId, MenuId, CompanyId) -- el acceso en una
        // compañía no debe filtrarse a otra compañía de la misma organización.
        var db = CrearContexto();
        var userId = Guid.NewGuid();
        var companiaConAcceso = Guid.NewGuid();
        var otraCompania = Guid.NewGuid();

        var menu = new Menu { OriginModule = "Ventas", Code = "Ordenes", Name = "Órdenes", PagePath = "/ventas/ordenes" };
        var action = new PermissionAction { Code = "VIEW", Name = "Ver" };
        var profile = new Profile { Name = "Vendedor" };
        db.Menus.Add(menu);
        db.Actions.Add(action);
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();

        db.ProfileActions.Add(new ProfileAction { ProfileId = profile.Id, ActionId = action.Id });
        db.UserMenuProfiles.Add(new UserMenuProfile { UserId = userId, MenuId = menu.Id, ProfileId = profile.Id, CompanyId = companiaConAcceso });
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db, userId, otraCompania, isAdmin: false);

        Assert.False(await servicio.HasActionAsync("Ventas.Ordenes", "VIEW"));
    }
}
