using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Administracion;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Administracion;

public sealed class LegacyExternalConnectionBackfillTests
{
    private static PortalSaasDbContext NuevoContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<Company> SembrarCompaniaAsync(PortalSaasDbContext db)
    {
        var c = new Company
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            Code = "C",
            Name = "C",
            DatabaseName = "C_DB",
            ServiceLayerUrl = "https://sl.local",
            IntegrationUsername = "svc",
            IntegrationSecretKey = "enc",
            Country = "CL",
        };
        db.Companies.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    [Fact]
    public async Task DesduplicaDosModulosQueApuntanAlaMismaBD()
    {
        await using var db = NuevoContexto();
        var c = await SembrarCompaniaAsync(db);
        db.ModuleExternalConnections.AddRange(
            new ModuleExternalConnection { CompanyId = c.Id, ModuleCode = "Wms", EngineType = "sqlserver", Host = "h", Port = 1, DatabaseName = "d", TechnicalUsername = "u", TechnicalSecretKey = "e", IsActive = true },
            new ModuleExternalConnection { CompanyId = c.Id, ModuleCode = "Rendiciones", EngineType = "sqlserver", Host = "h", Port = 1, DatabaseName = "d", TechnicalUsername = "u", TechnicalSecretKey = "e", IsActive = true });
        await db.SaveChangesAsync();

        var creados = await new LegacyExternalConnectionBackfill(db, NullLogger<LegacyExternalConnectionBackfill>.Instance).RunAsync(default);

        Assert.Equal(2, creados);
        Assert.Single(db.CompanyExternalConnections);
        Assert.Equal(2, db.CompanyModuleConnections.Count());
    }

    [Fact]
    public async Task EsIdempotente()
    {
        await using var db = NuevoContexto();
        var c = await SembrarCompaniaAsync(db);
        db.ModuleExternalConnections.Add(new ModuleExternalConnection { CompanyId = c.Id, ModuleCode = "Wms", EngineType = "postgres", Host = "h", Port = 5432, DatabaseName = "d", TechnicalUsername = "u", TechnicalSecretKey = "e", IsActive = true });
        await db.SaveChangesAsync();
        var backfill = new LegacyExternalConnectionBackfill(db, NullLogger<LegacyExternalConnectionBackfill>.Instance);

        await backfill.RunAsync(default);
        var segunda = await backfill.RunAsync(default);

        Assert.Equal(0, segunda);
        Assert.Single(db.CompanyExternalConnections);
        Assert.Single(db.CompanyModuleConnections);
    }
}
