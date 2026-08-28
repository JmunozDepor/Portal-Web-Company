using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Correo;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

// Herramienta de uso LOCAL ÚNICAMENTE -- nunca commitear credenciales reales, nunca
// correr esto en CI. Lee todo desde variables de entorno; no hay ningún secreto
// hardcodeado acá ni en ningún archivo versionado. Ver
// docs/06-AUTENTICACION-Y-PREFERENCIAS.md §7 para el detalle de qué hace y qué NO
// verifica todavía.

string? Env(string name) => Environment.GetEnvironmentVariable(name);

string Require(string name)
{
    var value = Env(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        Console.Error.WriteLine($"Falta la variable de entorno {name}. Ver tools/PortalSaas.Tools.EmailSmokeTest/README.md.");
        Environment.Exit(1);
    }
    return value!;
}

var provider = Require("PORTALSAAS_TEST_PROVIDER").Trim().ToLowerInvariant();
var senderEmail = Require("PORTALSAAS_TEST_SENDER_EMAIL");
var recipientEmail = Require("PORTALSAAS_TEST_RECIPIENT_EMAIL");
var senderDisplayName = Env("PORTALSAAS_TEST_SENDER_DISPLAY_NAME");

string providerConfigJson;
if (provider == EmailProviderType.Microsoft365)
{
    var config = new
    {
        tenantId = Require("PORTALSAAS_TEST_MS_TENANT_ID"),
        clientId = Require("PORTALSAAS_TEST_MS_CLIENT_ID"),
        clientSecret = Require("PORTALSAAS_TEST_MS_CLIENT_SECRET"),
    };
    providerConfigJson = JsonSerializer.Serialize(config);
}
else if (provider == EmailProviderType.GoogleWorkspace)
{
    var serviceAccountJsonPath = Require("PORTALSAAS_TEST_GOOGLE_SERVICE_ACCOUNT_JSON_PATH");
    if (!File.Exists(serviceAccountJsonPath))
    {
        Console.Error.WriteLine($"No se encontró el archivo {serviceAccountJsonPath} (PORTALSAAS_TEST_GOOGLE_SERVICE_ACCOUNT_JSON_PATH).");
        return 1;
    }

    using var serviceAccountDoc = JsonDocument.Parse(File.ReadAllText(serviceAccountJsonPath));
    var clientEmail = serviceAccountDoc.RootElement.GetProperty("client_email").GetString()!;
    var privateKeyPem = serviceAccountDoc.RootElement.GetProperty("private_key").GetString()!;

    providerConfigJson = JsonSerializer.Serialize(new { clientEmail, privateKeyPem });
}
else
{
    Console.Error.WriteLine($"PORTALSAAS_TEST_PROVIDER='{provider}' no reconocido. Usar '{EmailProviderType.Microsoft365}' o '{EmailProviderType.GoogleWorkspace}'.");
    return 1;
}

// Clave maestra generada al vuelo -- esta corrida cifra y descifra en el mismo
// proceso, no hace falta persistirla ni que coincida con ninguna clave real.
var masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:MasterSecretKey"] = masterKey })
    .Build();

var services = new ServiceCollection();
services.AddHttpClient();
using var serviceProvider = services.BuildServiceProvider();
var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

var secretos = new SecretoCifradoService(configuration);

using var db = new PortalSaasDbContext(new DbContextOptionsBuilder<PortalSaasDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString())
    .Options);

var org = new Organization { LegalName = "Prueba manual de correo", Slug = "prueba-manual-correo", Country = "CL" };
db.Organizations.Add(org);
await db.SaveChangesAsync();

db.EmailSettings.Add(new EmailSettings
{
    OrganizationId = org.Id,
    Provider = provider,
    SenderEmail = senderEmail,
    SenderDisplayName = senderDisplayName,
    EncryptedProviderConfig = secretos.Encrypt(providerConfigJson),
    IsActive = true,
});
await db.SaveChangesAsync();

var emailSender = new EmailSenderService(db, secretos, httpClientFactory);

Console.WriteLine($"Enviando correo de prueba vía '{provider}' desde {senderEmail} hacia {recipientEmail}...");

try
{
    await emailSender.SendAsync(org.Id, new EmailMessage(
        recipientEmail,
        "Prueba de PortalSaas — envío de correo",
        $"<p>Este es un correo de prueba enviado por <code>PortalSaas.Tools.EmailSmokeTest</code>.</p><p>Proveedor: <b>{provider}</b>.</p>"));

    Console.WriteLine("ÉXITO -- el proveedor aceptó el correo.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FALLÓ el envío:");
    Console.Error.WriteLine(ex);
    return 1;
}
