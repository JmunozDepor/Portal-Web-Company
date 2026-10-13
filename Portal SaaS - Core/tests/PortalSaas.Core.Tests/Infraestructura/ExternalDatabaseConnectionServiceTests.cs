using Microsoft.EntityFrameworkCore;
using Npgsql;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Infraestructura;

public sealed class ExternalDatabaseConnectionServiceTests
{
    private sealed class SecretosIdentidad : ISecretoCifradoService
    {
        public string Encrypt(string plainText) => "enc:" + plainText;
        public string Decrypt(string cipherText) => cipherText.StartsWith("enc:") ? cipherText[4..] : cipherText;
    }

    private static PortalSaasDbContext NuevoContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ExternalDatabaseConnectionService Crear(PortalSaasDbContext db) => new(db, new SecretosIdentidad());

    private static Company NuevaCompania(Guid orgId, long instanceId, string code) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = orgId, InstanceId = instanceId, Code = code, Name = code,
        DatabaseName = "SBO", ServiceLayerUrl = "https://x", IntegrationUsername = "x", IntegrationSecretKey = "x", Country = "CL",
    };

    private static Instance NuevaInstancia(Guid orgId, string name, bool activa = true) => new()
    {
        OrganizationId = orgId, Name = name, Host = "pg.local", Port = 5432,
        EngineType = InstanceEngineType.Postgres, TechnicalUsername = "inst_user",
        TechnicalSecretKey = "enc:inst_pass", IsActive = activa,
    };

    [Fact]
    public async Task ResolveConnectionAsync_SinCredencialPropia_UsaLaDeLaInstancia()
    {
        await using var db = NuevoContexto();
        var orgId = Guid.NewGuid();
        var inst = NuevaInstancia(orgId, "pg");
        db.Instances.Add(inst);
        await db.SaveChangesAsync();
        var company = NuevaCompania(orgId, inst.Id, "DEPOR");
        db.Companies.Add(company);
        db.CompanyModuleConnections.Add(new CompanyModuleConnection
        {
            CompanyId = company.Id, ModuleCode = "Rendiciones", InstanceId = inst.Id, DatabaseName = "ps_rendiciones",
        });
        await db.SaveChangesAsync();

        var r = await Crear(db).ResolveConnectionAsync("Rendiciones", company.Id);

        Assert.Equal(ExternalDatabaseEngineType.Postgres, r.EngineType);
        var b = new NpgsqlConnectionStringBuilder(r.ConnectionString);
        Assert.Equal("pg.local", b.Host);
        Assert.Equal("ps_rendiciones", b.Database);
        Assert.Equal("inst_user", b.Username);
        Assert.Equal("inst_pass", b.Password);
    }

    [Fact]
    public async Task ResolveConnectionAsync_ConCredencialPropia_LaUsaEnVezDeLaDeLaInstancia()
    {
        await using var db = NuevoContexto();
        var orgId = Guid.NewGuid();
        var inst = NuevaInstancia(orgId, "pg");
        db.Instances.Add(inst);
        await db.SaveChangesAsync();
        var company = NuevaCompania(orgId, inst.Id, "DEPOR");
        db.Companies.Add(company);
        db.CompanyModuleConnections.Add(new CompanyModuleConnection
        {
            CompanyId = company.Id, ModuleCode = "Wms", InstanceId = inst.Id, DatabaseName = "ps_wms",
            TechnicalUsername = "wms_user", TechnicalSecretKey = "enc:wms_pass",
        });
        await db.SaveChangesAsync();

        var r = await Crear(db).ResolveConnectionAsync("Wms", company.Id);

        var b = new NpgsqlConnectionStringBuilder(r.ConnectionString);
        Assert.Equal("wms_user", b.Username);
        Assert.Equal("wms_pass", b.Password);
    }

    [Fact]
    public async Task ResolveConnectionAsync_SinAsignacion_LanzaMensajePresentable()
    {
        await using var db = NuevoContexto();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).ResolveConnectionAsync("Rendiciones", Guid.NewGuid()));

        Assert.Contains("Rendiciones", ex.Message);
        Assert.Contains("Administración", ex.Message);
    }

    [Fact]
    public async Task ResolveConnectionAsync_InstanciaInactiva_LanzaNombrandoLaInstancia()
    {
        await using var db = NuevoContexto();
        var orgId = Guid.NewGuid();
        var inst = NuevaInstancia(orgId, "pg-apagado", activa: false);
        db.Instances.Add(inst);
        await db.SaveChangesAsync();
        var company = NuevaCompania(orgId, inst.Id, "DEPOR");
        db.Companies.Add(company);
        db.CompanyModuleConnections.Add(new CompanyModuleConnection
        {
            CompanyId = company.Id, ModuleCode = "SellOut", InstanceId = inst.Id, DatabaseName = "sellout",
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).ResolveConnectionAsync("SellOut", company.Id));

        Assert.Contains("pg-apagado", ex.Message);
    }

    [Fact]
    public async Task ListActiveCompanyIdsAsync_SoloCompaniasDelModuloConInstanciaActiva()
    {
        await using var db = NuevoContexto();
        var orgId = Guid.NewGuid();
        var activa = NuevaInstancia(orgId, "pg");
        var inactiva = NuevaInstancia(orgId, "pg-off", activa: false);
        db.Instances.AddRange(activa, inactiva);
        await db.SaveChangesAsync();
        var a = NuevaCompania(orgId, activa.Id, "A");
        var b = NuevaCompania(orgId, activa.Id, "B");
        db.Companies.AddRange(a, b);
        db.CompanyModuleConnections.AddRange(
            new CompanyModuleConnection { CompanyId = a.Id, ModuleCode = "Rendiciones", InstanceId = activa.Id, DatabaseName = "r" },
            new CompanyModuleConnection { CompanyId = b.Id, ModuleCode = "Rendiciones", InstanceId = inactiva.Id, DatabaseName = "r" },
            new CompanyModuleConnection { CompanyId = b.Id, ModuleCode = "OtroModulo", InstanceId = activa.Id, DatabaseName = "o" });
        await db.SaveChangesAsync();

        var r = await Crear(db).ListActiveCompanyIdsAsync("Rendiciones");

        var unica = Assert.Single(r);
        Assert.Equal(a.Id, unica.CompanyId);
        Assert.Equal(orgId, unica.OrganizationId);
    }
}
