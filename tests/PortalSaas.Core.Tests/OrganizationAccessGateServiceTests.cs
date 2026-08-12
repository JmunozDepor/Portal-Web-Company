using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Comercial;
using PortalSaas.Core.Comercial.Licenciamiento;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

public class OrganizationAccessGateServiceTests
{
    // Par de claves ECDSA compartido por todos los tests -- CheckLicenseAsync valida
    // el SignedStatusToken (no Status/ExpiresAt en crudo, ver su doc-comment), así que
    // cada test on-premise necesita un token realmente firmado para pasar la
    // verificación, igual que lo haría LicenseActivationService en producción.
    private static readonly ECDsa Keys = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly string PrivateKeyBase64 = Convert.ToBase64String(Keys.ExportPkcs8PrivateKey());
    private static readonly string PublicKeyBase64 = Convert.ToBase64String(Keys.ExportSubjectPublicKeyInfo());

    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options, new NullOrganizationScopeProvider());

    private static IConfiguration CrearConfiguracion(int offlineGraceDays = 15) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Licensing:SigningPrivateKey"] = PrivateKeyBase64,
            ["Licensing:CentralPublicKey"] = PublicKeyBase64,
            ["Licensing:OfflineGraceDays"] = offlineGraceDays.ToString(),
        })
        .Build();

    private static OrganizationAccessGateService CrearServicio(PortalSaasDbContext db, int offlineGraceDays = 15)
    {
        var configuracion = CrearConfiguracion(offlineGraceDays);
        var tokenService = new LicenseTokenService(configuracion);
        return new OrganizationAccessGateService(db, tokenService, configuracion);
    }

    /// <summary>Firma y aplica un SignedStatusToken a la licencia, simulando un heartbeat exitoso contra el central.</summary>
    private static void FirmarLicencia(OnPremiseLicense license, string status, DateTimeOffset expiresAt, long planId = 1, DateTimeOffset? signedAt = null)
    {
        var tokenService = new LicenseTokenService(CrearConfiguracion());
        var payload = new LicenseStatusPayload(license.OrganizationId, planId, null, null, null, status, expiresAt, DateTimeOffset.UtcNow);
        license.SignedStatusToken = tokenService.Sign(payload);
        license.SignedStatusUpdatedAt = signedAt ?? DateTimeOffset.UtcNow;
    }

    [Fact]
    public async Task CheckAccess_Saas_SinSuscripcion_Deniega()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "SaaS sin suscripción", Slug = "saas-sin-sub", Country = "CL", Mode = OrganizationMode.Saas };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db);
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

        var servicio = CrearServicio(db);
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

        var servicio = CrearServicio(db);
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

        var servicio = CrearServicio(db);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.False(resultado.IsAllowed);
        Assert.Contains("no tiene una licencia", resultado.Reason);
    }

    [Fact]
    public async Task CheckAccess_OnPremise_SinActivar_Deniega()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "On-premise sin activar", Slug = "onprem-sin-activar", Country = "CL", Mode = OrganizationMode.OnPremise };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        // Licencia dada de alta pero nunca activada contra el central -- sin
        // SignedStatusToken, aunque Status/ExpiresAt digan "todo bien".
        db.OnPremiseLicenses.Add(new OnPremiseLicense
        {
            OrganizationId = org.Id,
            ActivationKey = "KEY-SIN-ACTIVAR",
            Status = OnPremiseLicenseStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
        });
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.False(resultado.IsAllowed);
        Assert.Contains("no se ha activado", resultado.Reason);
    }

    [Fact]
    public async Task CheckAccess_OnPremise_TokenFirmadoValidoYVigente_Permite()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "On-premise ok", Slug = "onprem-ok", Country = "CL", Mode = OrganizationMode.OnPremise };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var license = new OnPremiseLicense { OrganizationId = org.Id, ActivationKey = "KEY-1" };
        FirmarLicencia(license, OnPremiseLicenseStatus.Active, DateTimeOffset.UtcNow.AddYears(1));
        db.OnPremiseLicenses.Add(license);
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.True(resultado.IsAllowed);
    }

    [Fact]
    public async Task CheckAccess_OnPremise_ColumnasEditadasAMano_NoBastanSinFirmaValida()
    {
        // Confirma el punto central del diseño: lo que decide es el token firmado, no
        // las columnas Status/ExpiresAt -- editarlas a mano (ej. restaurar un backup
        // con otro ExpiresAt) no debe alcanzar para pasar el gate.
        var db = CrearContexto();
        var org = new Organization { LegalName = "On-premise manipulada", Slug = "onprem-manipulada", Country = "CL", Mode = OrganizationMode.OnPremise };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var license = new OnPremiseLicense
        {
            OrganizationId = org.Id,
            ActivationKey = "KEY-MANIPULADA",
            Status = OnPremiseLicenseStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
            SignedStatusToken = "token-invalido-editado-a-mano",
            SignedStatusUpdatedAt = DateTimeOffset.UtcNow,
        };
        db.OnPremiseLicenses.Add(license);
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.False(resultado.IsAllowed);
        Assert.Contains("inválido", resultado.Reason);
    }

    [Fact]
    public async Task CheckAccess_OnPremise_TokenVencidoEnElPayload_Deniega()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "On-premise vencida", Slug = "onprem-vencida", Country = "CL", Mode = OrganizationMode.OnPremise };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var license = new OnPremiseLicense { OrganizationId = org.Id, ActivationKey = "KEY-2" };
        FirmarLicencia(license, OnPremiseLicenseStatus.Active, DateTimeOffset.UtcNow.AddDays(-1));
        db.OnPremiseLicenses.Add(license);
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.False(resultado.IsAllowed);
        Assert.Contains("venció", resultado.Reason);
    }

    [Fact]
    public async Task CheckAccess_OnPremise_StatusRevocadoEnElPayload_Deniega()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "On-premise revocada", Slug = "onprem-revocada", Country = "CL", Mode = OrganizationMode.OnPremise };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var license = new OnPremiseLicense { OrganizationId = org.Id, ActivationKey = "KEY-3" };
        FirmarLicencia(license, OnPremiseLicenseStatus.Revoked, DateTimeOffset.UtcNow.AddYears(1));
        db.OnPremiseLicenses.Add(license);
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.False(resultado.IsAllowed);
        Assert.Contains("revoked", resultado.Reason);
    }

    [Fact]
    public async Task CheckAccess_OnPremise_SinContactoCentralMasAlladeLaGracia_Deniega()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "On-premise offline", Slug = "onprem-offline", Country = "CL", Mode = OrganizationMode.OnPremise };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var license = new OnPremiseLicense { OrganizationId = org.Id, ActivationKey = "KEY-4" };
        FirmarLicencia(license, OnPremiseLicenseStatus.Active, DateTimeOffset.UtcNow.AddYears(1), signedAt: DateTimeOffset.UtcNow.AddDays(-20));
        db.OnPremiseLicenses.Add(license);
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db, offlineGraceDays: 15);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.False(resultado.IsAllowed);
        Assert.Contains("no contacta al servidor central", resultado.Reason);
    }

    [Fact]
    public async Task CheckAccess_OnPremise_DentroDeLaGraciaOffline_Permite()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "On-premise offline reciente", Slug = "onprem-offline-reciente", Country = "CL", Mode = OrganizationMode.OnPremise };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var license = new OnPremiseLicense { OrganizationId = org.Id, ActivationKey = "KEY-5" };
        FirmarLicencia(license, OnPremiseLicenseStatus.Active, DateTimeOffset.UtcNow.AddYears(1), signedAt: DateTimeOffset.UtcNow.AddDays(-5));
        db.OnPremiseLicenses.Add(license);
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db, offlineGraceDays: 15);
        var resultado = await servicio.CheckAccessAsync(org.Id);

        Assert.True(resultado.IsAllowed);
    }
}
