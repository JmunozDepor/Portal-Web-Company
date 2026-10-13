using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Administracion;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Administracion;

public sealed class ModuleDatabaseCredentialNormalizerTests
{
    /// <summary>
    /// Simula AES-GCM: la misma clave cifrada dos veces da textos distintos ("a:x" vs "b:x"),
    /// pero descifra igual. Por eso el normalizador debe comparar en claro, no el cifrado.
    /// </summary>
    private sealed class SecretosConNonce : ISecretoCifradoService
    {
        public string Encrypt(string plainText) => "n:" + plainText;
        public string Decrypt(string cipherText) => cipherText[(cipherText.IndexOf(':') + 1)..];
    }

    private static PortalSaasDbContext NuevoContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<CompanyModuleConnection> SembrarAsync(
        PortalSaasDbContext db, string? usuarioPropio, string? clavePropiaCifrada)
    {
        var inst = new Instance
        {
            OrganizationId = Guid.NewGuid(), Name = "pg", Host = "h", Port = 5432,
            EngineType = InstanceEngineType.Postgres, TechnicalUsername = "app", TechnicalSecretKey = "a:clave",
        };
        db.Instances.Add(inst);
        await db.SaveChangesAsync();
        var b = new CompanyModuleConnection
        {
            CompanyId = Guid.NewGuid(), ModuleCode = "Rendiciones", InstanceId = inst.Id, DatabaseName = "r",
            TechnicalUsername = usuarioPropio, TechnicalSecretKey = clavePropiaCifrada,
        };
        db.CompanyModuleConnections.Add(b);
        await db.SaveChangesAsync();
        return b;
    }

    private static ModuleDatabaseCredentialNormalizer Crear(PortalSaasDbContext db) =>
        new(db, new SecretosConNonce(), NullLogger<ModuleDatabaseCredentialNormalizer>.Instance);

    [Fact]
    public async Task MismoUsuarioYMismaClaveEnClaro_BorraLaCredencialPropia()
    {
        await using var db = NuevoContexto();
        var b = await SembrarAsync(db, "app", "b:clave");

        var limpiados = await Crear(db).RunAsync(CancellationToken.None);

        Assert.Equal(1, limpiados);
        var recargado = await db.CompanyModuleConnections.AsNoTracking().SingleAsync(x => x.Id == b.Id);
        Assert.Null(recargado.TechnicalUsername);
        Assert.Null(recargado.TechnicalSecretKey);
    }

    [Fact]
    public async Task ClaveDistinta_ConservaLaCredencialPropia()
    {
        await using var db = NuevoContexto();
        var b = await SembrarAsync(db, "app", "b:otra-clave");

        var limpiados = await Crear(db).RunAsync(CancellationToken.None);

        Assert.Equal(0, limpiados);
        var recargado = await db.CompanyModuleConnections.AsNoTracking().SingleAsync(x => x.Id == b.Id);
        Assert.Equal("b:otra-clave", recargado.TechnicalSecretKey);
    }

    [Fact]
    public async Task UsuarioDistinto_ConservaLaCredencialPropia()
    {
        await using var db = NuevoContexto();
        await SembrarAsync(db, "otro_usuario", "b:clave");

        Assert.Equal(0, await Crear(db).RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SegundaCorrida_NoHaceNada()
    {
        await using var db = NuevoContexto();
        await SembrarAsync(db, "app", "b:clave");

        await Crear(db).RunAsync(CancellationToken.None);
        Assert.Equal(0, await Crear(db).RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ClaveDeInstanciaVacia_ConservaLaPropiaSinFallar()
    {
        await using var db = NuevoContexto();
        var b = await SembrarAsync(db, "app", "b:clave");
        var inst = await db.Instances.SingleAsync();
        inst.TechnicalSecretKey = "";
        await db.SaveChangesAsync();

        Assert.Equal(0, await Crear(db).RunAsync(CancellationToken.None));
        var recargado = await db.CompanyModuleConnections.AsNoTracking().SingleAsync(x => x.Id == b.Id);
        Assert.Equal("b:clave", recargado.TechnicalSecretKey);
    }
}
