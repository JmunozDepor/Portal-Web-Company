using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Administracion;
using PortalSaas.Core.Comercial;
using PortalSaas.Core.Comercial.Licenciamiento;
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

public class TenantUserAdminServiceTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(PortalSaasDbContext Db, Organization Org, Plan Plan)> CrearOrganizacionConPlanAsync(int? userLimit = null)
    {
        var db = CrearContexto();

        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        var plan = new Plan { Code = "TEST-PLAN", Name = "Plan de prueba", UserLimit = userLimit };
        db.Organizations.Add(org);
        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        db.Subscriptions.Add(new Subscription { OrganizationId = org.Id, PlanId = plan.Id, Status = SubscriptionStatus.Active });
        await db.SaveChangesAsync();

        return (db, org, plan);
    }

    private static TenantUserAdminService CrearServicio(PortalSaasDbContext db, Guid organizationId, Guid? userId = null)
    {
        // Estos tests solo ejercitan organizaciones SaaS -- la config de licenciamiento
        // no se usa en ese camino, basta con una vacía.
        var configuracion = new ConfigurationBuilder().Build();
        var contractLimitService = new ContractLimitService(db, new LicenseTokenService(configuracion), configuracion);
        return new(db, new CurrentUserContextFijo { UserId = userId ?? Guid.NewGuid(), OrganizationId = organizationId }, contractLimitService);
    }

    [Fact]
    public async Task CreateAsync_RespetaElLimiteDelPlan()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync(userLimit: 1);
        var servicio = CrearServicio(db, org.Id);

        var primero = await servicio.CreateAsync("u1", "u1@test.cl", "Password123!", isAdmin: false);
        Assert.True(primero.IsSuccess);

        var segundo = await servicio.CreateAsync("u2", "u2@test.cl", "Password123!", isAdmin: false);
        Assert.False(segundo.IsSuccess);
        Assert.Contains("1/1", segundo.Reason);
    }

    [Fact]
    public async Task CreateAsync_UsernameDuplicadoDentroDeLaOrganizacion_Rechaza()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync();
        var servicio = CrearServicio(db, org.Id);

        await servicio.CreateAsync("mismo", "a@test.cl", "Password123!", isAdmin: false);
        var resultado = await servicio.CreateAsync("mismo", "b@test.cl", "Password123!", isAdmin: false);

        Assert.False(resultado.IsSuccess);
    }

    [Fact]
    public async Task CreateAsync_MismoUsernameEnOtraOrganizacion_NoColisiona()
    {
        var (db, org1, _) = await CrearOrganizacionConPlanAsync();
        var org2 = new Organization { LegalName = "Otro cliente", Slug = "otro-cliente", Country = "CL" };
        var plan2 = new Plan { Code = "TEST-PLAN-2", Name = "Plan de prueba 2" };
        db.Organizations.Add(org2);
        db.Plans.Add(plan2);
        await db.SaveChangesAsync();
        db.Subscriptions.Add(new Subscription { OrganizationId = org2.Id, PlanId = plan2.Id, Status = SubscriptionStatus.Active });
        await db.SaveChangesAsync();

        var servicio1 = CrearServicio(db, org1.Id);
        var servicio2 = CrearServicio(db, org2.Id);

        var resultado1 = await servicio1.CreateAsync("mismo", "a@test.cl", "Password123!", isAdmin: false);
        var resultado2 = await servicio2.CreateAsync("mismo", "b@test.cl", "Password123!", isAdmin: false);

        Assert.True(resultado1.IsSuccess);
        Assert.True(resultado2.IsSuccess);
    }

    [Fact]
    public async Task UpdateAsync_AutoBloqueo_NoPuedeQuitarseElAdminAMismo()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync();
        var servicio = CrearServicio(db, org.Id, userId: Guid.NewGuid());
        var creado = await servicio.CreateAsync("admin1", "admin1@test.cl", "Password123!", isAdmin: true);

        var servicioComoElMismo = CrearServicio(db, org.Id, userId: creado.UserId!.Value);
        var resultado = await servicioComoElMismo.UpdateAsync(creado.UserId.Value, "admin1@test.cl", isAdmin: false, isActive: true, isLocked: false);

        Assert.False(resultado.IsSuccess);
    }

    [Fact]
    public async Task UpdateAsync_AutoBloqueo_NoPuedeDesactivarseAsiMismo()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync();
        var servicio = CrearServicio(db, org.Id);
        var creado = await servicio.CreateAsync("admin1", "admin1@test.cl", "Password123!", isAdmin: true);

        var servicioComoElMismo = CrearServicio(db, org.Id, userId: creado.UserId!.Value);
        var resultado = await servicioComoElMismo.UpdateAsync(creado.UserId.Value, "admin1@test.cl", isAdmin: true, isActive: false, isLocked: false);

        Assert.False(resultado.IsSuccess);
    }

    [Fact]
    public async Task UpdateAsync_OtroUsuario_SiPuedeQuitarleElAdmin()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync();
        var servicio = CrearServicio(db, org.Id);
        var creado = await servicio.CreateAsync("otro", "otro@test.cl", "Password123!", isAdmin: true);

        var resultado = await servicio.UpdateAsync(creado.UserId!.Value, "otro@test.cl", isAdmin: false, isActive: true, isLocked: false);

        Assert.True(resultado.IsSuccess);
    }

    [Fact]
    public async Task DeleteAsync_NoPuedeEliminarseAsiMismo()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync();
        var servicio = CrearServicio(db, org.Id);
        var creado = await servicio.CreateAsync("admin1", "admin1@test.cl", "Password123!", isAdmin: true);

        var servicioComoElMismo = CrearServicio(db, org.Id, userId: creado.UserId!.Value);
        var resultado = await servicioComoElMismo.DeleteAsync(creado.UserId.Value);

        Assert.False(resultado.IsSuccess);
    }

    [Fact]
    public async Task SavePermissionsAsync_CompaniaDeOtraOrganizacion_Rechaza()
    {
        var (db, org1, _) = await CrearOrganizacionConPlanAsync();
        var org2 = new Organization { LegalName = "Otro cliente", Slug = "otro-cliente-2", Country = "CL" };
        db.Organizations.Add(org2);
        var instancia = new Instance { OrganizationId = org2.Id, Name = "srv1", Host = "localhost", EngineType = InstanceEngineType.Hana, TechnicalUsername = "x", TechnicalSecretKey = "x" };
        db.Instances.Add(instancia);
        await db.SaveChangesAsync();

        var companiaAjena = new Company
        {
            OrganizationId = org2.Id,
            InstanceId = instancia.Id,
            Code = "AJENA",
            Name = "Compañía ajena",
            DatabaseName = "DB1",
            ServiceLayerUrl = "https://x",
            IntegrationUsername = "x",
            IntegrationSecretKey = "x",
            Country = "CL",
        };
        db.Companies.Add(companiaAjena);
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db, org1.Id);
        var creado = await servicio.CreateAsync("usuario1", "usuario1@test.cl", "Password123!", isAdmin: false);

        var resultado = await servicio.SavePermissionsAsync(creado.UserId!.Value, companiaAjena.Id, [], new Dictionary<long, long?>());

        Assert.False(resultado.IsSuccess);
    }

    [Fact]
    public async Task GetPermissionsAsync_UsuarioDeOtraOrganizacion_DevuelveNull()
    {
        var (db, org1, _) = await CrearOrganizacionConPlanAsync();
        var servicioOrg1 = CrearServicio(db, org1.Id);
        var creadoEnOrg1 = await servicioOrg1.CreateAsync("usuario1", "usuario1@test.cl", "Password123!", isAdmin: false);

        var org2 = new Organization { LegalName = "Otro cliente", Slug = "otro-cliente-3", Country = "CL" };
        db.Organizations.Add(org2);
        await db.SaveChangesAsync();
        var servicioOrg2 = CrearServicio(db, org2.Id);

        var resultado = await servicioOrg2.GetPermissionsAsync(creadoEnOrg1.UserId!.Value, Guid.NewGuid());

        Assert.Null(resultado);
    }

    [Fact]
    public async Task ListAsync_SoloDevuelveUsuariosDeLaOrganizacionActual()
    {
        var (db, org1, _) = await CrearOrganizacionConPlanAsync();
        var org2 = new Organization { LegalName = "Otro cliente", Slug = "otro-cliente-4", Country = "CL" };
        db.Organizations.Add(org2);
        await db.SaveChangesAsync();

        await CrearServicio(db, org1.Id).CreateAsync("de-org1", "org1@test.cl", "Password123!", isAdmin: false);
        await CrearServicio(db, org2.Id).CreateAsync("de-org2", "org2@test.cl", "Password123!", isAdmin: false);

        var lista = await CrearServicio(db, org1.Id).ListAsync();

        Assert.Single(lista);
        Assert.Equal("de-org1", lista[0].Username);
    }
}
