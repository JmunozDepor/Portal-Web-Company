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
}
