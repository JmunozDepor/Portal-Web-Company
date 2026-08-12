using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Tests.Infraestructura;

public class ExternalDatabaseConnectionServiceTests
{
    private static PortalSaasDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PortalSaasDbContext(options);
    }

    [Fact]
    public async Task ListActiveCompanyIdsAsync_devuelve_solo_filas_activas_del_modulo_pedido()
    {
        await using var db = CreateDb(nameof(ListActiveCompanyIdsAsync_devuelve_solo_filas_activas_del_modulo_pedido));

        var org = new Organization { Id = Guid.NewGuid(), LegalName = "Org Test", Slug = "org-test", Country = "CL" };
        var instance = new Instance { OrganizationId = org.Id, Name = "srv1", Host = "localhost", EngineType = InstanceEngineType.Hana, TechnicalUsername = "x", TechnicalSecretKey = "x" };
        var companyA = new Company { Id = Guid.NewGuid(), OrganizationId = org.Id, InstanceId = instance.Id, Code = "CMP1", Name = "Company A", DatabaseName = "db1", ServiceLayerUrl = "https://x", IntegrationUsername = "x", IntegrationSecretKey = "x", Country = "CL" };
        var companyB = new Company { Id = Guid.NewGuid(), OrganizationId = org.Id, InstanceId = instance.Id, Code = "CMP2", Name = "Company B", DatabaseName = "db2", ServiceLayerUrl = "https://x", IntegrationUsername = "x", IntegrationSecretKey = "x", Country = "CL" };
        db.Organizations.Add(org);
        db.Instances.Add(instance);
        db.Companies.AddRange(companyA, companyB);

        db.ModuleExternalConnections.AddRange(
            new ModuleExternalConnection
            {
                CompanyId = companyA.Id, ModuleCode = "Rendiciones", IsActive = true,
                EngineType = ModuleExternalConnectionEngineType.Postgres,
                Host = "h", Port = 5432, DatabaseName = "db", TechnicalUsername = "u", TechnicalSecretKey = "k",
            },
            new ModuleExternalConnection
            {
                CompanyId = companyB.Id, ModuleCode = "Rendiciones", IsActive = false,
                EngineType = ModuleExternalConnectionEngineType.Postgres,
                Host = "h", Port = 5432, DatabaseName = "db", TechnicalUsername = "u", TechnicalSecretKey = "k",
            },
            new ModuleExternalConnection
            {
                CompanyId = companyA.Id, ModuleCode = "OtroModulo", IsActive = true,
                EngineType = ModuleExternalConnectionEngineType.Postgres,
                Host = "h", Port = 5432, DatabaseName = "db", TechnicalUsername = "u", TechnicalSecretKey = "k",
            });
        await db.SaveChangesAsync();

        var sut = new ExternalDatabaseConnectionService(db, new FakeSecretoCifradoService());

        var result = await sut.ListActiveCompanyIdsAsync("Rendiciones");

        var item = Assert.Single(result);
        Assert.Equal(companyA.Id, item.CompanyId);
        Assert.Equal(org.Id, item.OrganizationId);
    }

    private sealed class FakeSecretoCifradoService : PortalSaas.Abstractions.Contratos.ISecretoCifradoService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
    }
}
