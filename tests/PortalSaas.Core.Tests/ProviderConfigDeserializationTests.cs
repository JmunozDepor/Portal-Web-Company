using System.Text.Json;
using PortalSaas.Core.Correo;
using Xunit;

namespace PortalSaas.Core.Tests;

/// <summary>
/// Regresión de un bug real encontrado probando el envío de correo contra un
/// Google Workspace real (24 jul 2026): los records Microsoft365ProviderConfig/
/// GoogleWorkspaceProviderConfig no tenían [JsonPropertyName], y
/// JsonSerializer.Deserialize hace match de nombre case-SENSITIVE por defecto --
/// como el JSON se produce en camelCase (docs/06-...md §7) y las propiedades C# son
/// PascalCase, todo deserializaba a null en silencio (sin excepción en el momento del
/// Deserialize) y recién explotaba más abajo, al intentar usar una PEM vacía
/// (`RSA.ImportFromPem`: "No supported key formats were found"). Estos tests llaman
/// exactamente el mismo `JsonSerializer.Deserialize&lt;T&gt;(json)` sin opciones que usa
/// EmailSenderService, para que una regresión futura se detecte acá, no en
/// producción contra un tenant real.
/// </summary>
public class ProviderConfigDeserializationTests
{
    [Fact]
    public void Microsoft365ProviderConfig_DeserializaDesdeElJsonCamelCaseQueProduceElToolDePrueba()
    {
        var config = JsonSerializer.Deserialize<Microsoft365ProviderConfig>(
            """{"tenantId":"t-123","clientId":"c-456","clientSecret":"s-789"}""");

        Assert.NotNull(config);
        Assert.Equal("t-123", config!.TenantId);
        Assert.Equal("c-456", config.ClientId);
        Assert.Equal("s-789", config.ClientSecret);
    }

    [Fact]
    public void GoogleWorkspaceProviderConfig_DeserializaDesdeElJsonCamelCaseQueProduceElToolDePrueba()
    {
        var config = JsonSerializer.Deserialize<GoogleWorkspaceProviderConfig>(
            """{"clientEmail":"cuenta@proyecto.iam.gserviceaccount.com","privateKeyPem":"-----BEGIN PRIVATE KEY-----\nABC\n-----END PRIVATE KEY-----\n"}""");

        Assert.NotNull(config);
        Assert.Equal("cuenta@proyecto.iam.gserviceaccount.com", config!.ClientEmail);
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", config.PrivateKeyPem);
    }
}
