using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Comercial;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

public class ModuleAccessServiceTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task GetContractedModuleCodes_ModuloCore_SiempreIncluido()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        db.Organizations.Add(org);
        db.PlatformModules.Add(new PlatformModule { Code = "Administracion", Name = "Administración", IsCore = true });
        await db.SaveChangesAsync();

        var servicio = new ModuleAccessService(db);
        var codigos = await servicio.GetContractedModuleCodesAsync(org.Id);

        Assert.Contains("Administracion", codigos);
    }

    [Fact]
    public async Task GetContractedModuleCodes_ModuloDelPlanActivo_Incluido()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        var plan = new Plan { Code = "GROWTH", Name = "Growth" };
        var modulo = new PlatformModule { Code = "Ventas", Name = "Ventas" };
        db.Organizations.Add(org);
        db.Plans.Add(plan);
        db.PlatformModules.Add(modulo);
        await db.SaveChangesAsync();

        db.PlanModules.Add(new PlanModule { PlanId = plan.Id, ModuleId = modulo.Id });
        db.Subscriptions.Add(new Subscription { OrganizationId = org.Id, PlanId = plan.Id, Status = SubscriptionStatus.Active });
        await db.SaveChangesAsync();

        var servicio = new ModuleAccessService(db);
        var codigos = await servicio.GetContractedModuleCodesAsync(org.Id);

        Assert.Contains("Ventas", codigos);
    }

    [Fact]
    public async Task GetContractedModuleCodes_ModuloNoIncluidoEnElPlanNiComoAddon_NoAparece()
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

        // El plan solo incluye Ventas -- Compras no está ni en el plan ni como add-on.
        db.PlanModules.Add(new PlanModule { PlanId = plan.Id, ModuleId = moduloVentas.Id });
        db.Subscriptions.Add(new Subscription { OrganizationId = org.Id, PlanId = plan.Id, Status = SubscriptionStatus.Active });
        await db.SaveChangesAsync();

        var servicio = new ModuleAccessService(db);
        var codigos = await servicio.GetContractedModuleCodesAsync(org.Id);

        Assert.Contains("Ventas", codigos);
        Assert.DoesNotContain("Compras", codigos);
    }

    [Fact]
    public async Task GetContractedModuleCodes_AddonPropioDeLaOrganizacion_Incluido()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        var modulo = new PlatformModule { Code = "Inventario", Name = "Inventario" };
        db.Organizations.Add(org);
        db.PlatformModules.Add(modulo);
        await db.SaveChangesAsync();

        db.OrganizationModules.Add(new OrganizationModule { OrganizationId = org.Id, ModuleId = modulo.Id });
        await db.SaveChangesAsync();

        var servicio = new ModuleAccessService(db);
        var codigos = await servicio.GetContractedModuleCodesAsync(org.Id);

        Assert.Contains("Inventario", codigos);
    }

    [Fact]
    public async Task GetContractedModuleCodes_AddonDeOtraOrganizacion_NoAfecta()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        var otraOrg = new Organization { LegalName = "Otro cliente", Slug = "otro-cliente", Country = "CL" };
        var modulo = new PlatformModule { Code = "Inventario", Name = "Inventario" };
        db.Organizations.Add(org);
        db.Organizations.Add(otraOrg);
        db.PlatformModules.Add(modulo);
        await db.SaveChangesAsync();

        db.OrganizationModules.Add(new OrganizationModule { OrganizationId = otraOrg.Id, ModuleId = modulo.Id });
        await db.SaveChangesAsync();

        var servicio = new ModuleAccessService(db);
        var codigos = await servicio.GetContractedModuleCodesAsync(org.Id);

        Assert.DoesNotContain("Inventario", codigos);
    }

    [Fact]
    public async Task GetContractedModuleCodes_OnPremiseConLicenciaVigente_UsaElPlanDeLaLicencia()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Cliente on-premise", Slug = "cliente-on-premise", Country = "CL", Mode = OrganizationMode.OnPremise };
        var plan = new Plan { Code = "OP-PLAN", Name = "Plan on-premise" };
        var modulo = new PlatformModule { Code = "Compras", Name = "Compras" };
        db.Organizations.Add(org);
        db.Plans.Add(plan);
        db.PlatformModules.Add(modulo);
        await db.SaveChangesAsync();

        db.PlanModules.Add(new PlanModule { PlanId = plan.Id, ModuleId = modulo.Id });
        db.OnPremiseLicenses.Add(new OnPremiseLicense
        {
            OrganizationId = org.Id,
            PlanId = plan.Id,
            ActivationKey = Guid.NewGuid().ToString(),
            Status = OnPremiseLicenseStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
        });
        await db.SaveChangesAsync();

        var servicio = new ModuleAccessService(db);
        var codigos = await servicio.GetContractedModuleCodesAsync(org.Id);

        Assert.Contains("Compras", codigos);
    }

    [Fact]
    public async Task GetContractedModuleCodes_ModuloExclusivoDeOtraOrganizacion_NuncaAparece()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        var dueno = new Organization { LegalName = "Dueño del módulo exclusivo", Slug = "dueno-exclusivo", Country = "CL" };
        var plan = new Plan { Code = "STARTER", Name = "Starter" };
        var moduloExclusivo = new PlatformModule { Code = "GestionDistribucionGastos", Name = "Gestión Distribución Gastos", ExclusiveOrganizationId = dueno.Id };
        db.Organizations.Add(org);
        db.Organizations.Add(dueno);
        db.Plans.Add(plan);
        db.PlatformModules.Add(moduloExclusivo);
        await db.SaveChangesAsync();

        // Aunque por error el módulo exclusivo se cuele en el plan Y como add-on directo
        // de "org", nunca debería aparecer contratado para nadie salvo su dueño real.
        db.PlanModules.Add(new PlanModule { PlanId = plan.Id, ModuleId = moduloExclusivo.Id });
        db.Subscriptions.Add(new Subscription { OrganizationId = org.Id, PlanId = plan.Id, Status = SubscriptionStatus.Active });
        db.OrganizationModules.Add(new OrganizationModule { OrganizationId = org.Id, ModuleId = moduloExclusivo.Id });
        await db.SaveChangesAsync();

        var servicio = new ModuleAccessService(db);
        var codigosOrg = await servicio.GetContractedModuleCodesAsync(org.Id);
        var codigosDueno = await servicio.GetContractedModuleCodesAsync(dueno.Id);

        Assert.DoesNotContain("GestionDistribucionGastos", codigosOrg);
        Assert.DoesNotContain("GestionDistribucionGastos", codigosDueno); // sin fila propia de contratación, ni el dueño lo tiene automático
    }

    [Fact]
    public async Task GetCatalogedModuleCodes_DevuelveTodosLosCodigosDelCatalogo()
    {
        var db = CrearContexto();
        db.PlatformModules.Add(new PlatformModule { Code = "Ventas", Name = "Ventas" });
        db.PlatformModules.Add(new PlatformModule { Code = "Compras", Name = "Compras" });
        await db.SaveChangesAsync();

        var servicio = new ModuleAccessService(db);
        var codigos = await servicio.GetCatalogedModuleCodesAsync();

        Assert.Equal(2, codigos.Count);
        Assert.Contains("Ventas", codigos);
        Assert.Contains("Compras", codigos);
    }
}
