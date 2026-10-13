// tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionServiceTests.cs
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Administracion;
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

    private sealed class ModuloFalso(string code, bool usaBase) : IModuloPortal
    {
        public string ModuleCode => code;
        public string Name => "Módulo " + code;
        public string Version => "1.0.0";
        public bool RequiresExternalDatabase => usaBase;
        public IEnumerable<MenuItemDefinition> GetMenu() => [];
        public void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services) { }
    }

    private static readonly IReadOnlyList<IModuloPortal> Modulos =
    [
        new ModuloFalso("Rendiciones", usaBase: true),
        new ModuloFalso("Compras", usaBase: false),
    ];

    private static PortalSaasDbContext NuevoContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CompanyExternalConnectionService Crear(PortalSaasDbContext db) =>
        new(db, new SecretosIdentidad(), () => Modulos);

    private sealed record Escenario(Guid OrgId, Guid CompanyId, long InstanceId, long InstanciaOtraOrgId, long InstanciaInactivaId);

    private static async Task<Escenario> SembrarAsync(PortalSaasDbContext db)
    {
        var orgId = Guid.NewGuid();
        var inst = new Instance
        {
            OrganizationId = orgId, Name = "pg", Host = "pg.local", Port = 5432,
            EngineType = InstanceEngineType.Postgres, TechnicalUsername = "app", TechnicalSecretKey = "enc:inst",
        };
        var inactiva = new Instance
        {
            OrganizationId = orgId, Name = "pg-off", Host = "pg2.local", Port = 5432,
            EngineType = InstanceEngineType.Postgres, TechnicalUsername = "app", TechnicalSecretKey = "enc:inst", IsActive = false,
        };
        var otraOrg = new Instance
        {
            OrganizationId = Guid.NewGuid(), Name = "ajena", Host = "x", Port = 5432,
            EngineType = InstanceEngineType.Postgres, TechnicalUsername = "x", TechnicalSecretKey = "enc:x",
        };
        db.Instances.AddRange(inst, inactiva, otraOrg);
        await db.SaveChangesAsync();
        var company = new Company
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, InstanceId = inst.Id, Code = "DEPOR", Name = "Depor",
            DatabaseName = "SBO", ServiceLayerUrl = "https://sl", IntegrationUsername = "svc", IntegrationSecretKey = "enc", Country = "CL",
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return new Escenario(orgId, company.Id, inst.Id, otraOrg.Id, inactiva.Id);
    }

    private static ModuleDatabaseBindingInput Input(long instanceId, string db = "ps_rendiciones") =>
        new() { InstanceId = instanceId, DatabaseName = db };

    [Fact]
    public async Task ListBindings_SoloModulosQueUsanBaseExterna_SinAsignar()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        var filas = await Crear(db).ListBindingsAsync(e.OrgId, e.CompanyId);

        var fila = Assert.Single(filas);
        Assert.Equal("Rendiciones", fila.ModuleCode);
        Assert.Null(fila.InstanceId);
        Assert.False(fila.HasOwnCredentials);
    }

    [Fact]
    public async Task ListInstances_SoloActivasDeLaOrganizacion()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        var instancias = await Crear(db).ListInstancesAsync(e.OrgId, e.CompanyId);

        var unica = Assert.Single(instancias);
        Assert.Equal(e.InstanceId, unica.Id);
        Assert.Equal(InstanceEngineType.Postgres, unica.EngineType);
    }

    [Fact]
    public async Task SetBinding_SinCredencialPropia_HeredaLaDeLaInstancia()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var svc = Crear(db);

        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanceId));

        var fila = await db.CompanyModuleConnections.AsNoTracking().SingleAsync();
        Assert.Equal(e.InstanceId, fila.InstanceId);
        Assert.Equal("ps_rendiciones", fila.DatabaseName);
        Assert.Null(fila.TechnicalUsername);
        Assert.Null(fila.TechnicalSecretKey);
        var dto = await svc.GetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones");
        Assert.Equal("pg", dto!.InstanceName);
        Assert.False(dto.HasOwnCredentials);
    }

    [Fact]
    public async Task SetBinding_ConCredencialPropia_LaCifraYNoLaExpone()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var svc = Crear(db);
        var input = Input(e.InstanceId);
        input.UseOwnCredentials = true;
        input.TechnicalUsername = "rend_user";
        input.TechnicalSecretKey = "clave-secreta";

        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", input);

        var fila = await db.CompanyModuleConnections.AsNoTracking().SingleAsync();
        Assert.Equal("rend_user", fila.TechnicalUsername);
        Assert.Equal("enc:clave-secreta", fila.TechnicalSecretKey);
        var dto = await svc.GetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones");
        Assert.True(dto!.HasOwnCredentials);
        Assert.Equal("rend_user", dto.OwnTechnicalUsername);
        Assert.DoesNotContain("clave-secreta", System.Text.Json.JsonSerializer.Serialize(dto));
    }

    [Fact]
    public async Task SetBinding_CredencialPropiaConClaveEnBlanco_ConservaLaAnterior()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var svc = Crear(db);
        var alta = Input(e.InstanceId);
        alta.UseOwnCredentials = true;
        alta.TechnicalUsername = "rend_user";
        alta.TechnicalSecretKey = "clave-1";
        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", alta);

        var edicion = Input(e.InstanceId, "ps_rendiciones_v2");
        edicion.UseOwnCredentials = true;
        edicion.TechnicalUsername = "rend_user";
        edicion.TechnicalSecretKey = "";
        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", edicion);

        var fila = await db.CompanyModuleConnections.AsNoTracking().SingleAsync();
        Assert.Equal("ps_rendiciones_v2", fila.DatabaseName);
        Assert.Equal("enc:clave-1", fila.TechnicalSecretKey);
    }

    [Fact]
    public async Task SetBinding_CredencialPropiaNuevaSinClave_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var input = Input(e.InstanceId);
        input.UseOwnCredentials = true;
        input.TechnicalUsername = "rend_user";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", input));

        Assert.Contains("clave", ex.Message);
        Assert.Empty(db.CompanyModuleConnections);
    }

    [Fact]
    public async Task SetBinding_VolverAHeredar_BorraLaCredencialPropia()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var svc = Crear(db);
        var alta = Input(e.InstanceId);
        alta.UseOwnCredentials = true;
        alta.TechnicalUsername = "rend_user";
        alta.TechnicalSecretKey = "clave-1";
        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", alta);

        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanceId));

        var fila = await db.CompanyModuleConnections.AsNoTracking().SingleAsync();
        Assert.Null(fila.TechnicalUsername);
        Assert.Null(fila.TechnicalSecretKey);
    }

    [Fact]
    public async Task SetBinding_EsUpsert_UnaSolaFilaPorModulo()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var svc = Crear(db);

        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanceId, "a"));
        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanceId, "b"));

        var fila = await db.CompanyModuleConnections.AsNoTracking().SingleAsync();
        Assert.Equal("b", fila.DatabaseName);
    }

    [Fact]
    public async Task SetBinding_InstanciaDeOtraOrganizacion_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanciaOtraOrgId)));
    }

    [Fact]
    public async Task SetBinding_InstanciaInactiva_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanciaInactivaId)));
    }

    [Fact]
    public async Task SetBinding_BaseEnBlanco_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanceId, "   ")));
    }

    [Fact]
    public async Task SetBinding_ModuloQueNoUsaBaseExterna_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).SetBindingAsync(e.OrgId, e.CompanyId, "Compras", Input(e.InstanceId)));
        Assert.Contains("Compras", ex.Message);
    }

    [Fact]
    public async Task GetBinding_ModuloQueNoUsaBaseExterna_DevuelveNull()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        Assert.Null(await Crear(db).GetBindingAsync(e.OrgId, e.CompanyId, "Compras"));
    }

    [Fact]
    public async Task ClearBinding_BorraLaFila()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var svc = Crear(db);
        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanceId));

        await svc.ClearBindingAsync(e.OrgId, e.CompanyId, "Rendiciones");

        Assert.Empty(db.CompanyModuleConnections);
    }

    [Fact]
    public async Task CompaniaDeOtraOrganizacion_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).ListBindingsAsync(Guid.NewGuid(), e.CompanyId));
    }

    [Fact]
    public async Task TestBinding_SinAsignacion_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).TestBindingAsync(e.OrgId, e.CompanyId, "Rendiciones"));
    }
}
