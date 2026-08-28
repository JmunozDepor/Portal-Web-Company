using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;
using PortalSaas.Integrations;
using Xunit;

namespace PortalSaas.Core.Tests.Integrations;

public class IntegrationConnectorConfigServiceTests
{
    private sealed class FakeSecretoCifradoService : PortalSaas.Abstractions.Contratos.ISecretoCifradoService
    {
        public string Encrypt(string plainText) => $"ENC[{plainText}]";
        public string Decrypt(string cipherText) => cipherText.Replace("ENC[", "").TrimEnd(']');
    }

    private static PortalSaasDbContext CrearContexto(string dbName)
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PortalSaasDbContext(options);
    }

    [Fact]
    public async Task GetDecryptedConfigAsync_ConDefinicionActivaQueMatchea_DevuelveConfigDescifrada()
    {
        var companyId = Guid.NewGuid();
        await using var db = CrearContexto(nameof(GetDecryptedConfigAsync_ConDefinicionActivaQueMatchea_DevuelveConfigDescifrada));
        var secreto = new FakeSecretoCifradoService();

        db.IntegrationDefinitions.Add(new IntegrationDefinition
        {
            CompanyId = companyId,
            Nombre = "WMS - Items (Subida WMS)",
            ModuloOrigen = "Wms",
            EntidadNegocio = "SapWms.Item.Subida",
            ConectorTipo = IntegrationConectorTipo.WmsCloud,
            ConectorConfigCifrado = secreto.Encrypt("""{"ApiUrl":"https://x"}"""),
            Direccion = IntegrationDireccion.Subida,
            Activo = true,
        });
        await db.SaveChangesAsync();

        var sut = new IntegrationConnectorConfigService(db, secreto);
        var resultado = await sut.GetDecryptedConfigAsync(companyId, "Wms", "WmsCloud", CancellationToken.None);

        Assert.Equal("""{"ApiUrl":"https://x"}""", resultado);
    }

    [Fact]
    public async Task GetDecryptedConfigAsync_SinDefinicionActivaQueMatchee_DevuelveNull()
    {
        var companyId = Guid.NewGuid();
        await using var db = CrearContexto(nameof(GetDecryptedConfigAsync_SinDefinicionActivaQueMatchee_DevuelveNull));
        var secreto = new FakeSecretoCifradoService();

        db.IntegrationDefinitions.Add(new IntegrationDefinition
        {
            CompanyId = companyId,
            Nombre = "WMS - Items (Subida WMS)",
            ModuloOrigen = "Wms",
            EntidadNegocio = "SapWms.Item.Subida",
            ConectorTipo = IntegrationConectorTipo.WmsCloud,
            ConectorConfigCifrado = secreto.Encrypt("{}"),
            Direccion = IntegrationDireccion.Subida,
            Activo = false, // inactiva -- no debe matchear
        });
        await db.SaveChangesAsync();

        var sut = new IntegrationConnectorConfigService(db, secreto);
        var resultado = await sut.GetDecryptedConfigAsync(companyId, "Wms", "WmsCloud", CancellationToken.None);

        Assert.Null(resultado);
    }
}
