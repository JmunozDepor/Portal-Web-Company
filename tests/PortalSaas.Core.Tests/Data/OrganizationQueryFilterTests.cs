using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Data;

public class OrganizationQueryFilterTests
{
    private sealed class FixedOrganizationScopeProvider(Guid? organizationId) : IOrganizationScopeProvider
    {
        public Guid? CurrentOrganizationId { get; } = organizationId;
    }

    private static async Task<(Guid org1, Guid org2)> SeedTwoOrganizationsAsync(string dbName)
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var seedContext = new PortalSaasDbContext(options, new FixedOrganizationScopeProvider(null));

        var org1 = new Organization { Id = Guid.NewGuid(), LegalName = "Org 1", Slug = "org-1", Country = "CL" };
        var org2 = new Organization { Id = Guid.NewGuid(), LegalName = "Org 2", Slug = "org-2", Country = "CL" };
        seedContext.Organizations.AddRange(org1, org2);

        seedContext.Profiles.Add(new Profile { Name = "Perfil Org 1", OrganizationId = org1.Id });
        seedContext.Profiles.Add(new Profile { Name = "Perfil Org 2", OrganizationId = org2.Id });
        await seedContext.SaveChangesAsync();

        return (org1.Id, org2.Id);
    }

    [Fact]
    public async Task Con_organizacion_actual_fijada_solo_ve_sus_propias_filas()
    {
        var dbName = Guid.NewGuid().ToString();
        var (org1, org2) = await SeedTwoOrganizationsAsync(dbName);

        var options = new DbContextOptionsBuilder<PortalSaasDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var scopedContext = new PortalSaasDbContext(options, new FixedOrganizationScopeProvider(org1));

        var profiles = await scopedContext.Profiles.ToListAsync();

        Assert.Single(profiles);
        Assert.Equal(org1, profiles[0].OrganizationId);
    }

    [Fact]
    public async Task Sin_organizacion_actual_ve_todas_las_filas_bypass_explicito_de_plataforma()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedTwoOrganizationsAsync(dbName);

        var options = new DbContextOptionsBuilder<PortalSaasDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var platformContext = new PortalSaasDbContext(options, new FixedOrganizationScopeProvider(null));

        var profiles = await platformContext.Profiles.ToListAsync();

        Assert.Equal(2, profiles.Count);
    }

    [Fact]
    public async Task IgnoreQueryFilters_permite_bypass_explicito_con_organizacion_fijada()
    {
        var dbName = Guid.NewGuid().ToString();
        var (org1, _) = await SeedTwoOrganizationsAsync(dbName);

        var options = new DbContextOptionsBuilder<PortalSaasDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var scopedContext = new PortalSaasDbContext(options, new FixedOrganizationScopeProvider(org1));

        var allProfiles = await scopedContext.Profiles.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, allProfiles.Count);
    }
}
