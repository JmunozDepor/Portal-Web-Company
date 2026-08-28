using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Modelos;
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

    private static PortalSaasDbContext NuevoContexto([CallerMemberName] string dbName = "")
        => CreateDb(dbName);

    [Fact]
    public async Task ListActiveCompanyIdsAsync_devuelve_solo_filas_activas_del_modulo_pedido()
    {
        await using var db = NuevoContexto();

        var org = new Organization { Id = Guid.NewGuid(), LegalName = "Org Test", Slug = "org-test", Country = "CL" };
        var instance = new Instance { OrganizationId = org.Id, Name = "srv1", Host = "localhost", EngineType = InstanceEngineType.Hana, TechnicalUsername = "x", TechnicalSecretKey = "x" };
        var companyA = new Company { Id = Guid.NewGuid(), OrganizationId = org.Id, InstanceId = instance.Id, Code = "CMP1", Name = "Company A", DatabaseName = "db1", ServiceLayerUrl = "https://x", IntegrationUsername = "x", IntegrationSecretKey = "x", Country = "CL" };
        var companyB = new Company { Id = Guid.NewGuid(), OrganizationId = org.Id, InstanceId = instance.Id, Code = "CMP2", Name = "Company B", DatabaseName = "db2", ServiceLayerUrl = "https://x", IntegrationUsername = "x", IntegrationSecretKey = "x", Country = "CL" };
        db.Organizations.Add(org);
        db.Instances.Add(instance);
        db.Companies.AddRange(companyA, companyB);
        await db.SaveChangesAsync();

        var connActivaA = new CompanyExternalConnection
        {
            CompanyId = companyA.Id, Nombre = "BD-A",
            Tipo = ExternalConnectionType.DbPostgres, Host = "h", Port = 5432,
            DatabaseName = "db", TechnicalUsername = "u", TechnicalSecretKey = "enc:p", IsActive = true,
        };
        var connInactivaB = new CompanyExternalConnection
        {
            CompanyId = companyB.Id, Nombre = "BD-B",
            Tipo = ExternalConnectionType.DbPostgres, Host = "h", Port = 5432,
            DatabaseName = "db", TechnicalUsername = "u", TechnicalSecretKey = "enc:p", IsActive = false,
        };
        var connActivaOtroModulo = new CompanyExternalConnection
        {
            CompanyId = companyA.Id, Nombre = "BD-Otro",
            Tipo = ExternalConnectionType.DbPostgres, Host = "h", Port = 5432,
            DatabaseName = "db", TechnicalUsername = "u", TechnicalSecretKey = "enc:p", IsActive = true,
        };
        db.CompanyExternalConnections.AddRange(connActivaA, connInactivaB, connActivaOtroModulo);
        await db.SaveChangesAsync();

        db.CompanyModuleConnections.AddRange(
            new CompanyModuleConnection { CompanyId = companyA.Id, ModuleCode = "Rendiciones", Purpose = "Default", ConnectionId = connActivaA.Id },
            new CompanyModuleConnection { CompanyId = companyB.Id, ModuleCode = "Rendiciones", Purpose = "Default", ConnectionId = connInactivaB.Id },
            new CompanyModuleConnection { CompanyId = companyA.Id, ModuleCode = "OtroModulo", Purpose = "Default", ConnectionId = connActivaOtroModulo.Id });
        await db.SaveChangesAsync();

        var sut = new ExternalDatabaseConnectionService(db, new FakeSecretos());

        var result = await sut.ListActiveCompanyIdsAsync("Rendiciones");

        var item = Assert.Single(result);
        Assert.Equal(companyA.Id, item.CompanyId);
        Assert.Equal(org.Id, item.OrganizationId);
    }

    [Fact]
    public async Task ResolveConnectionAsync_UsaElBindingDefaultDelCatalogo()
    {
        await using var db = NuevoContexto();
        var companyId = Guid.NewGuid();
        var conn = new CompanyExternalConnection
        {
            CompanyId = companyId, Nombre = "BD",
            Tipo = ExternalConnectionType.DbPostgres, Host = "h", Port = 5432,
            DatabaseName = "d", TechnicalUsername = "u", TechnicalSecretKey = "enc:p", IsActive = true,
        };
        db.CompanyExternalConnections.Add(conn);
        await db.SaveChangesAsync();
        db.CompanyModuleConnections.Add(new CompanyModuleConnection
        {
            CompanyId = companyId,
            ModuleCode = "Wms", Purpose = "Default", ConnectionId = conn.Id,
        });
        await db.SaveChangesAsync();

        var svc = new ExternalDatabaseConnectionService(db, new FakeSecretos());
        var r = await svc.ResolveConnectionAsync("Wms", companyId);

        Assert.Equal(ExternalDatabaseEngineType.Postgres, r.EngineType);
        Assert.Contains("Host=h", r.ConnectionString);
    }

    [Fact]
    public async Task ResolveConnectionAsync_SinBinding_Lanza()
    {
        await using var db = NuevoContexto();
        var svc = new ExternalDatabaseConnectionService(db, new FakeSecretos());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ResolveConnectionAsync("Wms", Guid.NewGuid()));
    }

    [Fact]
    public async Task ResolveConnectionAsync_ConPurpose_ResuelveElSlotIndicado()
    {
        await using var db = NuevoContexto();
        var companyId = Guid.NewGuid();
        var sap = new CompanyExternalConnection
        {
            CompanyId = companyId, Nombre = "SAP",
            Tipo = ExternalConnectionType.DbSqlServer, Host = "sap", Port = 1433,
            DatabaseName = "SBO", TechnicalUsername = "u", TechnicalSecretKey = "enc:p", IsActive = true,
        };
        db.CompanyExternalConnections.Add(sap);
        await db.SaveChangesAsync();
        db.CompanyModuleConnections.Add(new CompanyModuleConnection
        {
            CompanyId = companyId,
            ModuleCode = "Wms", Purpose = "SapSource", ConnectionId = sap.Id,
        });
        await db.SaveChangesAsync();

        var svc = new ExternalDatabaseConnectionService(db, new FakeSecretos());
        var r = await svc.ResolveConnectionAsync("Wms", companyId, "SapSource");
        Assert.Equal(ExternalDatabaseEngineType.SqlServer, r.EngineType);
    }

    private sealed class FakeSecretos : PortalSaas.Abstractions.Contratos.ISecretoCifradoService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
    }
}
