using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Correo;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

/// <summary>
/// Cubre solo el despacho/falla-cerrada de EmailSenderService (resolución de
/// email_settings, deserialización de configuración) -- el envío real vía Microsoft
/// Graph/Gmail API NO está cubierto acá porque exigiría credenciales reales o mockear
/// dos flujos OAuth completos; ver docs/06-...md §7 para el detalle de qué sí/no está
/// verificado.
/// </summary>
public class EmailSenderServiceTests
{
    /// <summary>Passthrough -- no cifra de verdad, alcanza para testear el despacho sin depender de ISecretoCifradoService real.</summary>
    private sealed class FakeSecretoCifradoService : ISecretoCifradoService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
    }

    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task SendAsync_SinEmailSettings_LanzaExcepcion()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Sin correo configurado", Slug = "sin-correo", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var servicio = new EmailSenderService(db, new FakeSecretoCifradoService(), new HttpClientFactoryStub());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.SendAsync(org.Id, new EmailMessage("x@y.cl", "Asunto", "<p>Cuerpo</p>")));
    }

    [Fact]
    public async Task SendAsync_ConEmailSettingsInactiva_LanzaExcepcion()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Correo desactivado", Slug = "correo-desactivado", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        db.EmailSettings.Add(new EmailSettings
        {
            OrganizationId = org.Id,
            Provider = EmailProviderType.Microsoft365,
            SenderEmail = "no-reply@cliente.cl",
            EncryptedProviderConfig = """{"tenantId":"t","clientId":"c","clientSecret":"s"}""",
            IsActive = false,
        });
        await db.SaveChangesAsync();

        var servicio = new EmailSenderService(db, new FakeSecretoCifradoService(), new HttpClientFactoryStub());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.SendAsync(org.Id, new EmailMessage("x@y.cl", "Asunto", "<p>Cuerpo</p>")));
    }

    [Fact]
    public async Task SendAsync_ConProveedorNoReconocido_LanzaExcepcion()
    {
        var db = CrearContexto();
        var org = new Organization { LegalName = "Proveedor raro", Slug = "proveedor-raro", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        db.EmailSettings.Add(new EmailSettings
        {
            OrganizationId = org.Id,
            Provider = "otro_proveedor_no_soportado",
            SenderEmail = "no-reply@cliente.cl",
            EncryptedProviderConfig = "{}",
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var servicio = new EmailSenderService(db, new FakeSecretoCifradoService(), new HttpClientFactoryStub());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.SendAsync(org.Id, new EmailMessage("x@y.cl", "Asunto", "<p>Cuerpo</p>")));
    }

    private sealed class HttpClientFactoryStub : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
