using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Comercial;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

file sealed class CurrentUserContextFijo : ICurrentUserContext
{
    public required Guid UserId { get; init; }
    public string Username => "usuario.prueba";
    public bool IsAdmin => false;
    public required Guid OrganizationId { get; init; }

    public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) => Task.FromResult(true);
}

public class OrganizationDocumentPermissionServiceTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options, new NullOrganizationScopeProvider());

    private static async Task<(PortalSaasDbContext Db, Organization Org)> CrearOrganizacionAsync()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return (db, org);
    }

    [Fact]
    public async Task IsCreateAllowed_SinFilaDeOverride_UsaElDefaultDelCatalogo()
    {
        var (db, org) = await CrearOrganizacionAsync();
        var currentUser = new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id };
        var servicio = new OrganizationDocumentPermissionService(db, currentUser);

        var permiteConDefaultTrue = await servicio.IsCreateAllowedAsync("Sales", "SalesOrder", defaultValue: true);
        var permiteConDefaultFalse = await servicio.IsCreateAllowedAsync("Purchase", "PurchaseOrder", defaultValue: false);

        Assert.True(permiteConDefaultTrue);
        Assert.False(permiteConDefaultFalse);
    }

    [Fact]
    public async Task IsCreateAllowed_ConOverrideFalse_BloqueaAunqueElDefaultSeaTrue()
    {
        var (db, org) = await CrearOrganizacionAsync();
        db.OrganizationDocumentPermissions.Add(new OrganizationDocumentPermission
        {
            OrganizationId = org.Id,
            Engine = "Sales",
            DocumentType = "SalesOrder",
            CanCreate = false,
        });
        await db.SaveChangesAsync();

        var currentUser = new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id };
        var servicio = new OrganizationDocumentPermissionService(db, currentUser);

        var permite = await servicio.IsCreateAllowedAsync("Sales", "SalesOrder", defaultValue: true);

        Assert.False(permite);
    }

    [Fact]
    public async Task IsCreateAllowed_ConOverrideTrue_PermiteAunqueElDefaultSeaFalse()
    {
        var (db, org) = await CrearOrganizacionAsync();
        db.OrganizationDocumentPermissions.Add(new OrganizationDocumentPermission
        {
            OrganizationId = org.Id,
            Engine = "Purchase",
            DocumentType = "PurchaseOrder",
            CanCreate = true,
        });
        await db.SaveChangesAsync();

        var currentUser = new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id };
        var servicio = new OrganizationDocumentPermissionService(db, currentUser);

        var permite = await servicio.IsCreateAllowedAsync("Purchase", "PurchaseOrder", defaultValue: false);

        Assert.True(permite);
    }

    [Fact]
    public async Task IsCreateAllowed_OverrideDeOtraOrganizacion_NoAfecta()
    {
        var (db, org) = await CrearOrganizacionAsync();
        var otraOrg = new Organization { LegalName = "Otro cliente", Slug = "otro-cliente", Country = "CL" };
        db.Organizations.Add(otraOrg);
        await db.SaveChangesAsync();

        db.OrganizationDocumentPermissions.Add(new OrganizationDocumentPermission
        {
            OrganizationId = otraOrg.Id,
            Engine = "Sales",
            DocumentType = "SalesOrder",
            CanCreate = false,
        });
        await db.SaveChangesAsync();

        var currentUser = new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id };
        var servicio = new OrganizationDocumentPermissionService(db, currentUser);

        var permite = await servicio.IsCreateAllowedAsync("Sales", "SalesOrder", defaultValue: true);

        Assert.True(permite);
    }
}
