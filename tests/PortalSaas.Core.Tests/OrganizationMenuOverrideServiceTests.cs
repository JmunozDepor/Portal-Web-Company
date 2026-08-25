using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Administracion;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

file sealed class CurrentUserContextFijo : ICurrentUserContext
{
    public required Guid UserId { get; init; }
    public string Username => "usuario.prueba";
    public bool IsAdmin => true;
    public required Guid OrganizationId { get; init; }

    public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) => Task.FromResult(true);
}

public class OrganizationMenuOverrideServiceTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

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
    public async Task SaveOverridesAsync_ConValores_CreaLaFila()
    {
        var (db, org, raiz, _) = await CrearOrganizacionConMenuAsync();
        var servicio = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id });

        await servicio.SaveOverridesAsync(new Dictionary<long, MenuOverrideInput>
        {
            [raiz.Id] = new MenuOverrideInput("Mis Ventas", 5, false),
        });

        var filas = await servicio.ListAsync();
        var fila = Assert.Single(filas, f => f.MenuId == raiz.Id);
        Assert.Equal("Mis Ventas", fila.CustomLabel);
        Assert.Equal(5, fila.CustomOrder);
        Assert.False(fila.IsHidden);
    }

    [Fact]
    public async Task SaveOverridesAsync_ValoresPorDefecto_BorraLaFilaExistente()
    {
        var (db, org, raiz, _) = await CrearOrganizacionConMenuAsync();
        var servicio = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id });

        await servicio.SaveOverridesAsync(new Dictionary<long, MenuOverrideInput> { [raiz.Id] = new MenuOverrideInput("Custom", 1, false) });
        Assert.Equal(1, await db.OrganizationMenuOverrides.CountAsync());

        await servicio.SaveOverridesAsync(new Dictionary<long, MenuOverrideInput> { [raiz.Id] = new MenuOverrideInput(null, null, false) });

        Assert.Equal(0, await db.OrganizationMenuOverrides.CountAsync());
    }

    [Fact]
    public async Task ListAsync_AislaEntreOrganizaciones()
    {
        var (db, org1, raiz, _) = await CrearOrganizacionConMenuAsync();
        var org2 = new Organization { LegalName = "Otro cliente", Slug = "otro-cliente", Country = "CL" };
        db.Organizations.Add(org2);
        await db.SaveChangesAsync();

        var servicioOrg1 = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org1.Id });
        var servicioOrg2 = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org2.Id });

        await servicioOrg1.SaveOverridesAsync(new Dictionary<long, MenuOverrideInput> { [raiz.Id] = new MenuOverrideInput("Solo Org1", null, false) });

        var filasOrg2 = await servicioOrg2.ListAsync();
        var filaOrg2 = Assert.Single(filasOrg2, f => f.MenuId == raiz.Id);
        Assert.Null(filaOrg2.CustomLabel);

        var filasOrg1 = await servicioOrg1.ListAsync();
        var filaOrg1 = Assert.Single(filasOrg1, f => f.MenuId == raiz.Id);
        Assert.Equal("Solo Org1", filaOrg1.CustomLabel);
    }

    [Fact]
    public async Task ListAsync_SinOverrides_DevuelveTodosLosNodosConValoresPorDefecto()
    {
        var (db, org, raiz, hijo) = await CrearOrganizacionConMenuAsync();
        var servicio = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id });

        var filas = await servicio.ListAsync();

        Assert.Equal(2, filas.Count);
        Assert.All(filas, f => Assert.Null(f.CustomLabel));
        Assert.All(filas, f => Assert.False(f.IsHidden));
        Assert.Contains(filas, f => f.MenuId == raiz.Id && f.Level == 0);
        Assert.Contains(filas, f => f.MenuId == hijo.Id && f.Level == 1);
    }
}
