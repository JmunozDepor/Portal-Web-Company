using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Administracion;
using PortalSaas.Core.Comercial;
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
        var servicio = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id }, new ModuleAccessService(db));

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
        var servicio = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id }, new ModuleAccessService(db));

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

        var servicioOrg1 = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org1.Id }, new ModuleAccessService(db));
        var servicioOrg2 = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org2.Id }, new ModuleAccessService(db));

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
        var servicio = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id }, new ModuleAccessService(db));

        var filas = await servicio.ListAsync();

        Assert.Equal(2, filas.Count);
        Assert.All(filas, f => Assert.Null(f.CustomLabel));
        Assert.All(filas, f => Assert.False(f.IsHidden));
        Assert.Contains(filas, f => f.MenuId == raiz.Id && f.Level == 0);
        Assert.Contains(filas, f => f.MenuId == hijo.Id && f.Level == 1);
    }

    [Fact]
    public async Task SaveOverridesAsync_OcultarNodoDeAdministracion_NoPersisteIsHidden()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var administracion = new Menu { OriginModule = "Administracion", Code = "raiz", Name = "Administración", Order = 0, Level = 0, IsActive = true };
        db.Menus.Add(administracion);
        await db.SaveChangesAsync();

        var servicio = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id }, new ModuleAccessService(db));

        // Un admin intenta ocultar el nodo raíz de Administración -- si se guardara,
        // dejaría a la organización sin forma visual de revertirlo (auto-lockout).
        await servicio.SaveOverridesAsync(new Dictionary<long, MenuOverrideInput>
        {
            [administracion.Id] = new MenuOverrideInput(null, null, true),
        });

        var filas = await servicio.ListAsync();
        var fila = Assert.Single(filas, f => f.MenuId == administracion.Id);
        Assert.False(fila.IsHidden);

        // Como los demás campos también quedan en su valor por defecto, no debería
        // haberse creado ningún override en absoluto.
        Assert.Equal(0, await db.OrganizationMenuOverrides.CountAsync());
    }

    [Fact]
    public async Task SaveOverridesAsync_OcultarNodoDeAdministracionConOtrosCambios_ConservaOverridePeroSinOcultar()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var administracion = new Menu { OriginModule = "Administracion", Code = "raiz", Name = "Administración", Order = 0, Level = 0, IsActive = true };
        db.Menus.Add(administracion);
        await db.SaveChangesAsync();

        var servicio = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id }, new ModuleAccessService(db));

        await servicio.SaveOverridesAsync(new Dictionary<long, MenuOverrideInput>
        {
            [administracion.Id] = new MenuOverrideInput("Mi Administración", 3, true),
        });

        var filas = await servicio.ListAsync();
        var fila = Assert.Single(filas, f => f.MenuId == administracion.Id);
        Assert.Equal("Mi Administración", fila.CustomLabel);
        Assert.Equal(3, fila.CustomOrder);
        Assert.False(fila.IsHidden);
    }

    [Fact]
    public async Task ListAsync_ConCatalogoComercialCargado_ExcluyeModuloNoContratado()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        var plan = new Plan { Code = "STARTER", Name = "Starter" };
        var moduloVentas = new PlatformModule { Code = "Ventas", Name = "Ventas" };
        var moduloCompras = new PlatformModule { Code = "Compras", Name = "Compras" };
        db.Organizations.Add(org);
        db.Plans.Add(plan);
        db.PlatformModules.Add(moduloVentas);
        db.PlatformModules.Add(moduloCompras);
        await db.SaveChangesAsync();

        // El plan solo incluye Ventas -- Compras no está ni en el plan ni como add-on,
        // así que la organización no lo tiene contratado.
        db.PlanModules.Add(new PlanModule { PlanId = plan.Id, ModuleId = moduloVentas.Id });
        db.Subscriptions.Add(new Subscription { OrganizationId = org.Id, PlanId = plan.Id, Status = SubscriptionStatus.Active });
        await db.SaveChangesAsync();

        var nodoVentas = new Menu { OriginModule = "Ventas", Code = "raiz", Name = "Ventas", Order = 0, Level = 0, IsActive = true };
        var nodoCompras = new Menu { OriginModule = "Compras", Code = "raiz", Name = "Compras", Order = 0, Level = 0, IsActive = true };
        db.Menus.Add(nodoVentas);
        db.Menus.Add(nodoCompras);
        await db.SaveChangesAsync();

        var servicio = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id }, new ModuleAccessService(db));

        var filas = await servicio.ListAsync();

        Assert.Contains(filas, f => f.MenuId == nodoVentas.Id);
        Assert.DoesNotContain(filas, f => f.MenuId == nodoCompras.Id);
    }
}
