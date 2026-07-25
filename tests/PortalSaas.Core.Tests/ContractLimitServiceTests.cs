using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Comercial;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

public class ContractLimitServiceTests
{
    private static PortalSaasDbContext CrearContexto()
    {
        // Base InMemory nueva por instancia de test (Guid en el nombre) -- evita que
        // un test contamine a otro, sin depender de un Postgres/SQL Server real.
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PortalSaasDbContext(options);
    }

    private static async Task<(PortalSaasDbContext Db, Organization Org, Plan Plan)> CrearOrganizacionConPlanAsync(
        int? userLimit = null, int? companyLimit = null, int? monthlyTransactionLimit = null)
    {
        var db = CrearContexto();

        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        var plan = new Plan
        {
            Code = "TEST-PLAN",
            Name = "Plan de prueba",
            UserLimit = userLimit,
            CompanyLimit = companyLimit,
            MonthlyTransactionLimit = monthlyTransactionLimit,
        };

        db.Organizations.Add(org);
        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        db.Subscriptions.Add(new Subscription
        {
            OrganizationId = org.Id,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
        });
        await db.SaveChangesAsync();

        return (db, org, plan);
    }

    private static async Task<(PortalSaasDbContext Db, Organization Org, Plan Plan)> CrearOrganizacionOnPremiseConLicenciaAsync(
        int? userLimit = null, string licenseStatus = OnPremiseLicenseStatus.Active, DateTimeOffset? expiresAt = null)
    {
        var db = CrearContexto();

        var org = new Organization { LegalName = "Cliente on-premise", Slug = "cliente-on-premise", Country = "CL", Mode = OrganizationMode.OnPremise };
        var plan = new Plan { Code = "TEST-PLAN-OP", Name = "Plan de prueba on-premise", UserLimit = userLimit };

        db.Organizations.Add(org);
        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        db.OnPremiseLicenses.Add(new OnPremiseLicense
        {
            OrganizationId = org.Id,
            PlanId = plan.Id,
            ActivationKey = Guid.NewGuid().ToString(),
            Status = licenseStatus,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddYears(1),
        });
        await db.SaveChangesAsync();

        return (db, org, plan);
    }

    [Fact]
    public async Task CheckUserLimit_OnPremiseSinLicencia_SiempreDeniega()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Sin licencia", Slug = "sin-licencia", Country = "CL", Mode = OrganizationMode.OnPremise };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var servicio = new ContractLimitService(db);
        var resultado = await servicio.CheckUserLimitAsync(org.Id);

        Assert.False(resultado.IsAllowed);
    }

    [Fact]
    public async Task CheckUserLimit_OnPremiseConLicenciaActiva_UsaElPlanDeLaLicencia()
    {
        var (db, org, _) = await CrearOrganizacionOnPremiseConLicenciaAsync(userLimit: 2);
        db.Users.Add(new User { OrganizationId = org.Id, Username = "u1", Email = "u1@test.cl", PasswordHash = "x", PasswordSalt = "x" });
        await db.SaveChangesAsync();

        var servicio = new ContractLimitService(db);
        var resultado = await servicio.CheckUserLimitAsync(org.Id);

        Assert.True(resultado.IsAllowed);
    }

    [Fact]
    public async Task CheckUserLimit_OnPremiseLimiteAlcanzado_Deniega()
    {
        var (db, org, _) = await CrearOrganizacionOnPremiseConLicenciaAsync(userLimit: 1);
        db.Users.Add(new User { OrganizationId = org.Id, Username = "u1", Email = "u1@test.cl", PasswordHash = "x", PasswordSalt = "x" });
        await db.SaveChangesAsync();

        var servicio = new ContractLimitService(db);
        var resultado = await servicio.CheckUserLimitAsync(org.Id);

        Assert.False(resultado.IsAllowed);
    }

    [Theory]
    [InlineData(OnPremiseLicenseStatus.Revoked)]
    [InlineData(OnPremiseLicenseStatus.Expired)]
    public async Task CheckUserLimit_OnPremiseLicenciaNoActiva_Deniega(string status)
    {
        var (db, org, _) = await CrearOrganizacionOnPremiseConLicenciaAsync(licenseStatus: status);

        var servicio = new ContractLimitService(db);
        var resultado = await servicio.CheckUserLimitAsync(org.Id);

        Assert.False(resultado.IsAllowed);
    }

    [Fact]
    public async Task CheckUserLimit_OnPremiseLicenciaVencida_Deniega()
    {
        var (db, org, _) = await CrearOrganizacionOnPremiseConLicenciaAsync(expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        var servicio = new ContractLimitService(db);
        var resultado = await servicio.CheckUserLimitAsync(org.Id);

        Assert.False(resultado.IsAllowed);
    }

    [Fact]
    public async Task CheckUserLimit_SinSuscripcionActiva_SiempreDeniega()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Sin suscripción", Slug = "sin-suscripcion", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var servicio = new ContractLimitService(db);
        var resultado = await servicio.CheckUserLimitAsync(org.Id);

        Assert.False(resultado.IsAllowed);
        Assert.Contains("plan vigente", resultado.Reason);
    }

    [Fact]
    public async Task CheckUserLimit_PlanSinLimite_SiemprePermite()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync(userLimit: null);

        var servicio = new ContractLimitService(db);
        var resultado = await servicio.CheckUserLimitAsync(org.Id);

        Assert.True(resultado.IsAllowed);
    }

    [Fact]
    public async Task CheckUserLimit_DebajoDelLimite_Permite()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync(userLimit: 2);
        db.Users.Add(new User { OrganizationId = org.Id, Username = "usuario1", Email = "usuario1@test.cl", PasswordHash = "x", PasswordSalt = "x" });
        await db.SaveChangesAsync();

        var servicio = new ContractLimitService(db);
        var resultado = await servicio.CheckUserLimitAsync(org.Id);

        Assert.True(resultado.IsAllowed);
    }

    [Fact]
    public async Task CheckUserLimit_LimiteAlcanzado_Deniega()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync(userLimit: 2);
        db.Users.Add(new User { OrganizationId = org.Id, Username = "usuario1", Email = "usuario1@test.cl", PasswordHash = "x", PasswordSalt = "x" });
        db.Users.Add(new User { OrganizationId = org.Id, Username = "usuario2", Email = "usuario2@test.cl", PasswordHash = "x", PasswordSalt = "x" });
        await db.SaveChangesAsync();

        var servicio = new ContractLimitService(db);
        var resultado = await servicio.CheckUserLimitAsync(org.Id);

        Assert.False(resultado.IsAllowed);
        Assert.Contains("2/2", resultado.Reason);
    }

    [Fact]
    public async Task CheckUserLimit_UsuarioInactivoNoCuentaContraElLimite()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync(userLimit: 1);
        db.Users.Add(new User { OrganizationId = org.Id, Username = "inactivo", Email = "inactivo@test.cl", PasswordHash = "x", PasswordSalt = "x", IsActive = false });
        await db.SaveChangesAsync();

        var servicio = new ContractLimitService(db);
        var resultado = await servicio.CheckUserLimitAsync(org.Id);

        Assert.True(resultado.IsAllowed);
    }

    [Fact]
    public async Task CheckCompanyLimit_LimiteAlcanzado_Deniega()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync(companyLimit: 1);
        var instancia = new Instance { OrganizationId = org.Id, Name = "srv1", Host = "localhost", EngineType = InstanceEngineType.Hana, TechnicalUsername = "x", TechnicalSecretKey = "x" };
        db.Instances.Add(instancia);
        await db.SaveChangesAsync();

        db.Companies.Add(new Company
        {
            OrganizationId = org.Id,
            InstanceId = instancia.Id,
            Code = "EMP1",
            Name = "Empresa 1",
            DatabaseName = "DB1",
            ServiceLayerUrl = "https://x",
            IntegrationUsername = "x",
            IntegrationSecretKey = "x",
            Country = "CL",
        });
        await db.SaveChangesAsync();

        var servicio = new ContractLimitService(db);
        var resultado = await servicio.CheckCompanyLimitAsync(org.Id);

        Assert.False(resultado.IsAllowed);
    }

    [Fact]
    public async Task CheckMonthlyTransactionLimit_SumaCorrectaDelPeriodo_Deniega()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync(monthlyTransactionLimit: 100);
        db.UsageMetrics.Add(new UsageMetric { OrganizationId = org.Id, MetricName = "sap_transaction", Period = "202607", Value = 60 });
        db.UsageMetrics.Add(new UsageMetric { OrganizationId = org.Id, MetricName = "sap_transaction", Period = "202607", Value = 45 });
        // Mismo mes, otra métrica -- no debe sumarse al total.
        db.UsageMetrics.Add(new UsageMetric { OrganizationId = org.Id, MetricName = "document_created", Period = "202607", Value = 1000 });
        await db.SaveChangesAsync();

        var servicio = new ContractLimitService(db);
        var resultado = await servicio.CheckMonthlyTransactionLimitAsync(org.Id, "202607", "sap_transaction");

        Assert.False(resultado.IsAllowed);
        Assert.Contains("105", resultado.Reason);
    }

    [Fact]
    public async Task CheckMonthlyTransactionLimit_PeriodoDistintoNoSuma()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync(monthlyTransactionLimit: 100);
        db.UsageMetrics.Add(new UsageMetric { OrganizationId = org.Id, MetricName = "sap_transaction", Period = "202606", Value = 99 });
        await db.SaveChangesAsync();

        var servicio = new ContractLimitService(db);
        var resultado = await servicio.CheckMonthlyTransactionLimitAsync(org.Id, "202607", "sap_transaction");

        Assert.True(resultado.IsAllowed);
    }
}
