using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Comercial;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

file sealed class CurrentUserContextNoAdmin : ICurrentUserContext
{
    public required Guid UserId { get; init; }
    public string Username => "usuario.prueba";
    public bool IsAdmin => false;
    public required Guid OrganizationId { get; init; }

    public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) => Task.FromResult(true);
}

file sealed class CurrentUserContextAdmin : ICurrentUserContext
{
    public required Guid UserId { get; init; }
    public string Username => "admin.prueba";
    public bool IsAdmin => true;
    public required Guid OrganizationId { get; init; }

    public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) => Task.FromResult(true);
}

file sealed class CurrentCompanyAccessorFijo : ICurrentCompanyAccessor
{
    public required Guid CompanyId { get; init; }
    public string Code => "TEST";
    public string Database => "TEST";
    public string ServiceLayerUrl => "http://test";
    public string Country => "CL";
    public bool HasCompany => true;
}

public class MenuNavigationServiceTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static MenuNavigationService CrearServicio(PortalSaasDbContext db, Guid organizationId, bool isAdmin = true) =>
        new(db, isAdmin
                ? new CurrentUserContextAdmin { UserId = Guid.NewGuid(), OrganizationId = organizationId }
                : new CurrentUserContextNoAdmin { UserId = Guid.NewGuid(), OrganizationId = organizationId },
            new CurrentCompanyAccessorFijo { CompanyId = Guid.NewGuid() },
            new ModuleAccessService(db));

    private static async Task<(PortalSaasDbContext Db, Organization Org, Menu Raiz, Menu Hijo)> CrearOrganizacionConMenuAsync()
    {
        var db = CrearContexto();

        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var raiz = new Menu { OriginModule = "Ventas", Code = "raiz", Name = "Ventas", Order = 0, Level = 0, IsActive = true };
        db.Menus.Add(raiz);
        await db.SaveChangesAsync();

        var hijo = new Menu { OriginModule = "Ventas", Code = "ordenes", Name = "Órdenes", Order = 0, Level = 1, IsActive = true, ParentMenuId = raiz.Id };
        db.Menus.Add(hijo);
        await db.SaveChangesAsync();

        return (db, org, raiz, hijo);
    }

    [Fact]
    public async Task GetVisibleMenuAsync_SinOverrides_ComportamientoIgualQueHoy()
    {
        var (db, org, raiz, hijo) = await CrearOrganizacionConMenuAsync();
        var servicio = CrearServicio(db, org.Id);

        var arbol = await servicio.GetVisibleMenuAsync();

        var raizVisible = Assert.Single(arbol);
        Assert.Equal("Ventas", raizVisible.Name);
        var hijoVisible = Assert.Single(raizVisible.Children);
        Assert.Equal("Órdenes", hijoVisible.Name);
    }

    [Fact]
    public async Task GetVisibleMenuAsync_ConCustomLabel_UsaElNombrePersonalizado()
    {
        var (db, org, raiz, _) = await CrearOrganizacionConMenuAsync();
        db.OrganizationMenuOverrides.Add(new OrganizationMenuOverride { OrganizationId = org.Id, MenuId = raiz.Id, CustomLabel = "Mis Ventas" });
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db, org.Id);
        var arbol = await servicio.GetVisibleMenuAsync();

        var raizVisible = Assert.Single(arbol);
        Assert.Equal("Mis Ventas", raizVisible.Name);
    }

    [Fact]
    public async Task GetVisibleMenuAsync_NodoOculto_DesaparaceConSuSubarbol()
    {
        var (db, org, raiz, hijo) = await CrearOrganizacionConMenuAsync();
        db.OrganizationMenuOverrides.Add(new OrganizationMenuOverride { OrganizationId = org.Id, MenuId = raiz.Id, IsHidden = true });
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db, org.Id);
        var arbol = await servicio.GetVisibleMenuAsync();

        Assert.Empty(arbol);
    }

    [Fact]
    public async Task GetVisibleMenuAsync_OverrideAplicaTambienAUnAdmin()
    {
        var (db, org, raiz, _) = await CrearOrganizacionConMenuAsync();
        db.OrganizationMenuOverrides.Add(new OrganizationMenuOverride { OrganizationId = org.Id, MenuId = raiz.Id, IsHidden = true });
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db, org.Id, isAdmin: true);
        var arbol = await servicio.GetVisibleMenuAsync();

        Assert.Empty(arbol);
    }

    [Fact]
    public async Task GetVisibleMenuAsync_OverrideDeOtraOrganizacion_NoAfecta()
    {
        var (db, org1, raiz, _) = await CrearOrganizacionConMenuAsync();
        var org2 = new Organization { LegalName = "Otro cliente", Slug = "otro-cliente", Country = "CL" };
        db.Organizations.Add(org2);
        await db.SaveChangesAsync();
        db.OrganizationMenuOverrides.Add(new OrganizationMenuOverride { OrganizationId = org2.Id, MenuId = raiz.Id, IsHidden = true });
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db, org1.Id);
        var arbol = await servicio.GetVisibleMenuAsync();

        Assert.Single(arbol);
    }

    private static MenuNavigationService CrearServicioNoAdmin(PortalSaasDbContext db, Guid organizationId, Guid userId, Guid companyId) =>
        new(db,
            new CurrentUserContextNoAdmin { UserId = userId, OrganizationId = organizationId },
            new CurrentCompanyAccessorFijo { CompanyId = companyId },
            new ModuleAccessService(db));

    private static async Task<long> CrearGrupoConNodoAsync(PortalSaasDbContext db, Guid organizationId, Guid userId, Guid companyId, long menuId, long? defaultProfileId)
    {
        var grupo = new MenuGroup { OrganizationId = organizationId, Name = $"Grupo {Guid.NewGuid():N}", IsActive = true };
        db.MenuGroups.Add(grupo);
        await db.SaveChangesAsync();

        db.MenuGroupItems.Add(new MenuGroupItem { MenuGroupId = grupo.Id, MenuId = menuId, DefaultProfileId = defaultProfileId });
        db.UserMenuGroups.Add(new UserMenuGroup { UserId = userId, MenuGroupId = grupo.Id, CompanyId = companyId });
        await db.SaveChangesAsync();

        return grupo.Id;
    }

    [Fact]
    public async Task GetVisibleMenuAsync_NoAdmin_ItemDeGrupoSinPerfilPorDefecto_IgualSeVe()
    {
        var (db, org, raiz, hijo) = await CrearOrganizacionConMenuAsync();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        // Nodo hoja incluido en el grupo del usuario PERO sin DefaultProfileId -- antes
        // esto lo dejaba invisible; ahora se ve (solo navegación, sin permisos).
        await CrearGrupoConNodoAsync(db, org.Id, userId, companyId, hijo.Id, defaultProfileId: null);

        var servicio = CrearServicioNoAdmin(db, org.Id, userId, companyId);
        var arbol = await servicio.GetVisibleMenuAsync();

        var raizVisible = Assert.Single(arbol);
        Assert.Equal("Ventas", raizVisible.Name);
        var hijoVisible = Assert.Single(raizVisible.Children);
        Assert.Equal("Órdenes", hijoVisible.Name);
    }

    [Fact]
    public async Task GetVisibleMenuAsync_NoAdmin_ItemDeGrupoConPerfilPorDefecto_SigueVisible()
    {
        var (db, org, _, hijo) = await CrearOrganizacionConMenuAsync();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        var perfil = new Profile { Name = "Lectura" };
        db.Profiles.Add(perfil);
        await db.SaveChangesAsync();

        await CrearGrupoConNodoAsync(db, org.Id, userId, companyId, hijo.Id, defaultProfileId: perfil.Id);

        var servicio = CrearServicioNoAdmin(db, org.Id, userId, companyId);
        var arbol = await servicio.GetVisibleMenuAsync();

        var hijoVisible = Assert.Single(Assert.Single(arbol).Children);
        Assert.Equal("Órdenes", hijoVisible.Name);
    }

    [Fact]
    public async Task GetVisibleMenuAsync_NoAdmin_SinGrupoNiAsignacion_NoVeNada()
    {
        var (db, org, _, _) = await CrearOrganizacionConMenuAsync();

        var servicio = CrearServicioNoAdmin(db, org.Id, Guid.NewGuid(), Guid.NewGuid());
        var arbol = await servicio.GetVisibleMenuAsync();

        Assert.Empty(arbol);
    }
}
