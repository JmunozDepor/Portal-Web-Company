using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Comercial;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

public class OrganizationAccessGateServiceTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task CheckAccess_Saas_SinSuscripcion_Deniega()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "SaaS sin suscripción", Slug = "saas-sin-sub", Country = "CL", Mode = OrganizationMode.Saas };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var servicio = new OrganizationAccessGateService(db);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.False(resultado.IsAllowed);
        Assert.Contains("no tiene una suscripción", resultado.Reason);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trial, true)]
    [InlineData(SubscriptionStatus.Active, true)]
    [InlineData(SubscriptionStatus.PastDue, false)]
    [InlineData(SubscriptionStatus.Cancelled, false)]
    public async Task CheckAccess_Saas_SegunEstadoDeLaSuscripcion(string status, bool debePermitir)
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "SaaS", Slug = "saas-" + status, Country = "CL", Mode = OrganizationMode.Saas };
        var plan = new Plan { Code = "PLAN-" + status, Name = "Plan" };
        db.Organizations.Add(org);
        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        db.Subscriptions.Add(new Subscription { OrganizationId = org.Id, PlanId = plan.Id, Status = status });
        await db.SaveChangesAsync();

        var servicio = new OrganizationAccessGateService(db);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.Equal(debePermitir, resultado.IsAllowed);
    }

    [Fact]
    public async Task CheckAccess_Saas_UsaLaSuscripcionMasReciente()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "SaaS multi-sub", Slug = "saas-multi", Country = "CL", Mode = OrganizationMode.Saas };
        var plan = new Plan { Code = "PLAN-X", Name = "Plan" };
        db.Organizations.Add(org);
        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        db.Subscriptions.Add(new Subscription
        {
            OrganizationId = org.Id,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Cancelled,
            StartedAt = DateTimeOffset.UtcNow.AddMonths(-2),
        });
        db.Subscriptions.Add(new Subscription
        {
            OrganizationId = org.Id,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var servicio = new OrganizationAccessGateService(db);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.True(resultado.IsAllowed);
    }

    [Fact]
    public async Task CheckAccess_OnPremise_SinLicencia_Deniega()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "On-premise sin licencia", Slug = "onprem-sin-lic", Country = "CL", Mode = OrganizationMode.OnPremise };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var servicio = new OrganizationAccessGateService(db);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.False(resultado.IsAllowed);
        Assert.Contains("no tiene una licencia", resultado.Reason);
    }

    [Fact]
    public async Task CheckAccess_OnPremise_LicenciaActivaYVigente_Permite()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "On-premise ok", Slug = "onprem-ok", Country = "CL", Mode = OrganizationMode.OnPremise };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        db.OnPremiseLicenses.Add(new OnPremiseLicense
        {
            OrganizationId = org.Id,
            ActivationKey = "KEY-1",
            Status = OnPremiseLicenseStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
        });
        await db.SaveChangesAsync();

        var servicio = new OrganizationAccessGateService(db);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.True(resultado.IsAllowed);
    }

    [Fact]
    public async Task CheckAccess_OnPremise_LicenciaVencida_Deniega()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "On-premise vencida", Slug = "onprem-vencida", Country = "CL", Mode = OrganizationMode.OnPremise };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        db.OnPremiseLicenses.Add(new OnPremiseLicense
        {
            OrganizationId = org.Id,
            ActivationKey = "KEY-2",
            Status = OnPremiseLicenseStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var servicio = new OrganizationAccessGateService(db);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.False(resultado.IsAllowed);
        Assert.Contains("venció", resultado.Reason);
    }

    [Fact]
    public async Task CheckAccess_OnPremise_LicenciaRevocada_Deniega()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "On-premise revocada", Slug = "onprem-revocada", Country = "CL", Mode = OrganizationMode.OnPremise };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        db.OnPremiseLicenses.Add(new OnPremiseLicense
        {
            OrganizationId = org.Id,
            ActivationKey = "KEY-3",
            Status = OnPremiseLicenseStatus.Revoked,
            ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
        });
        await db.SaveChangesAsync();

        var servicio = new OrganizationAccessGateService(db);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.False(resultado.IsAllowed);
        Assert.Contains("revoked", resultado.Reason);
    }
}
