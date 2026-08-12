using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PortalSaas.Core.Comercial;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Comercial;

public class ModuleAccessServiceCachingTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options, new NullOrganizationScopeProvider());

    [Fact]
    public async Task GetContractedModuleCodesAsync_resultado_queda_cacheado_hasta_invalidar_por_Remove()
    {
        var db = CrearContexto();
        var cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        var moduloExistente = new PlatformModule { Code = "Ventas", Name = "Ventas" };
        db.Organizations.Add(org);
        db.PlatformModules.Add(moduloExistente);
        await db.SaveChangesAsync();

        db.OrganizationModules.Add(new OrganizationModule { OrganizationId = org.Id, ModuleId = moduloExistente.Id });
        await db.SaveChangesAsync();

        var servicio = new ModuleAccessService(db, cache);

        var primeraLectura = await servicio.GetContractedModuleCodesAsync(org.Id);
        Assert.Contains("Ventas", primeraLectura);
        Assert.DoesNotContain("Inventario", primeraLectura);

        // Se agrega un módulo nuevo DESPUÉS de la primera lectura -- mientras el
        // caché siga vigente, GetContractedModuleCodesAsync debe seguir devolviendo
        // el resultado viejo (sin "Inventario"), aunque la base ya cambió.
        var moduloNuevo = new PlatformModule { Code = "Inventario", Name = "Inventario" };
        db.PlatformModules.Add(moduloNuevo);
        await db.SaveChangesAsync();
        db.OrganizationModules.Add(new OrganizationModule { OrganizationId = org.Id, ModuleId = moduloNuevo.Id });
        await db.SaveChangesAsync();

        var segundaLecturaAntesDeInvalidar = await servicio.GetContractedModuleCodesAsync(org.Id);
        Assert.DoesNotContain("Inventario", segundaLecturaAntesDeInvalidar);

        cache.Remove($"modulos-contratados:{org.Id}");

        var lecturaDespuesDeInvalidar = await servicio.GetContractedModuleCodesAsync(org.Id);
        Assert.Contains("Ventas", lecturaDespuesDeInvalidar);
        Assert.Contains("Inventario", lecturaDespuesDeInvalidar);
    }
}
