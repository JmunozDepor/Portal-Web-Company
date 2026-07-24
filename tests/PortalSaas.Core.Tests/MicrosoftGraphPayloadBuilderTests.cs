using System.Text.Json;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Correo;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

public class MicrosoftGraphPayloadBuilderTests
{
    [Fact]
    public void BuildSendMailJson_ArmaElPayloadConLosDatosCorrectos()
    {
        var settings = new EmailSettings
        {
            SenderEmail = "no-reply@comercialdepor.cl",
            SenderDisplayName = "Portal SAP",
        };
        var message = new EmailMessage("destinatario@cliente.cl", "Recupera tu clave", "<p>Hola</p>");

        var json = MicrosoftGraphPayloadBuilder.BuildSendMailJson(settings, message);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Recupera tu clave", root.GetProperty("message").GetProperty("subject").GetString());
        Assert.Equal("HTML", root.GetProperty("message").GetProperty("body").GetProperty("contentType").GetString());
        Assert.Equal("<p>Hola</p>", root.GetProperty("message").GetProperty("body").GetProperty("content").GetString());
        Assert.Equal("destinatario@cliente.cl", root.GetProperty("message").GetProperty("toRecipients")[0]
            .GetProperty("emailAddress").GetProperty("address").GetString());
        Assert.Equal("no-reply@comercialdepor.cl", root.GetProperty("message").GetProperty("from")
            .GetProperty("emailAddress").GetProperty("address").GetString());
        Assert.False(root.GetProperty("saveToSentItems").GetBoolean());
    }
}
