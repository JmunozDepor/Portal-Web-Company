using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Administracion;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Administracion;

public sealed class CompanyExternalConnectionServiceTests
{
    private sealed class SecretosIdentidad : ISecretoCifradoService
    {
        public string Encrypt(string plainText) => "enc:" + plainText;
        public string Decrypt(string cipherText) => cipherText.StartsWith("enc:") ? cipherText[4..] : cipherText;
    }

    private static PortalSaasDbContext NuevoContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(Guid orgId, Guid companyId)> SembrarCompaniaAsync(PortalSaasDbContext db)
    {
        var orgId = Guid.NewGuid();
        var company = new Company
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Code = "DEPOR",
            Name = "Depor",
            DatabaseName = "DEPOR_DB",
            ServiceLayerUrl = "https://sl.local",
            IntegrationUsername = "svc",
            IntegrationSecretKey = "enc",
            Country = "CL",
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return (orgId, company.Id);
    }

    // PluginManager real no tiene constructor sin parámetros (necesita ILogger) -- el
    // helper del brief usaba new PluginManager(); se ajusta al ILogger nulo. Ningún
    // test de esta task carga plugins reales, así que ModulosCargados queda vacío.
    private static CompanyExternalConnectionService Crear(PortalSaasDbContext db) =>
        new(db, new SecretosIdentidad(), new PluginManager(NullLogger<PluginManager>.Instance));

    private static ExternalConnectionEditModel ModeloDb(string nombre) => new()
    {
        Nombre = nombre, Tipo = ExternalConnectionType.DbSqlServer,
        Host = "h", Port = 1433, DatabaseName = "d", TechnicalUsername = "u", TechnicalSecretKey = "secreto",
    };

    [Fact]
    public async Task Create_YLuego_Get_NoExponeSecreto()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);

        var id = await svc.CreateAsync(orgId, companyId, ModeloDb("BD WMS"));
        var dto = await svc.GetAsync(orgId, companyId, id);

        Assert.NotNull(dto);
        Assert.Equal("BD WMS", dto!.Nombre);
        Assert.DoesNotContain("secreto", System.Text.Json.JsonSerializer.Serialize(dto));
    }

    [Fact]
    public async Task Update_SinSecreto_ConservaElAnterior()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);
        var id = await svc.CreateAsync(orgId, companyId, ModeloDb("BD"));

        var edit = ModeloDb("BD"); edit.TechnicalSecretKey = null; edit.Host = "nuevo-host";
        await svc.UpdateAsync(orgId, companyId, id, edit);

        var fila = await db.CompanyExternalConnections.AsNoTracking().FirstAsync(c => c.Id == id);
        Assert.Equal("enc:secreto", fila.TechnicalSecretKey);
        Assert.Equal("nuevo-host", fila.Host);
    }

    [Fact]
    public async Task Create_NombreDuplicadoEnLaMismaCompania_Rechaza()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);
        await svc.CreateAsync(orgId, companyId, ModeloDb("BD"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync(orgId, companyId, ModeloDb("bd")));
    }

    [Fact]
    public async Task Create_HttpApiSinBaseUrl_Rechaza()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);

        var m = new ExternalConnectionEditModel { Nombre = "API", Tipo = ExternalConnectionType.HttpApi };
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(orgId, companyId, m));
    }

    [Fact]
    public async Task List_NoDevuelveConexionesDeOtraCompania()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var (otraOrg, otraCompany) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);
        await svc.CreateAsync(orgId, companyId, ModeloDb("mia"));
        await svc.CreateAsync(otraOrg, otraCompany, ModeloDb("ajena"));

        var lista = await svc.ListAsync(orgId, companyId);
        Assert.Single(lista);
        Assert.Equal("mia", lista[0].Nombre);
    }

    [Fact]
    public async Task Delete_ConBindingActivo_Rechaza()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);
        var id = await svc.CreateAsync(orgId, companyId, ModeloDb("BD"));
        await svc.SetBindingAsync(orgId, companyId, "Wms", "Default", id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteAsync(orgId, companyId, id));
    }

    [Fact]
    public async Task SetBinding_EsUpsertPorModuloYPurpose()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);
        var a = await svc.CreateAsync(orgId, companyId, ModeloDb("A"));
        var b = await svc.CreateAsync(orgId, companyId, ModeloDb("B"));

        await svc.SetBindingAsync(orgId, companyId, "Wms", "Default", a);
        await svc.SetBindingAsync(orgId, companyId, "Wms", "Default", b);

        var filas = await db.CompanyModuleConnections.Where(x => x.CompanyId == companyId && x.ModuleCode == "Wms").ToListAsync();
        Assert.Single(filas);
        Assert.Equal(b, filas[0].ConnectionId);
    }

    [Fact]
    public async Task Create_CompaniaDeOtraOrganizacion_Rechaza()
    {
        await using var db = NuevoContexto();
        var (_, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync(Guid.NewGuid(), companyId, ModeloDb("x")));
    }
}
