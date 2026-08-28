using System.Text.Json;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Correo;

/// <summary>
/// Arma el cuerpo JSON de `POST /users/{senderEmail}/sendMail` de Microsoft Graph --
/// separado en un método puro (sin HTTP) para poder testearlo sin red ni credenciales
/// reales. Referencia: https://learn.microsoft.com/graph/api/user-sendmail
/// </summary>
internal static class MicrosoftGraphPayloadBuilder
{
    public static string BuildSendMailJson(EmailSettings settings, EmailMessage message)
    {
        var payload = new
        {
            message = new
            {
                subject = message.Subject,
                body = new { contentType = "HTML", content = message.HtmlBody },
                toRecipients = new[] { new { emailAddress = new { address = message.ToEmail } } },
                from = new
                {
                    emailAddress = new
                    {
                        address = settings.SenderEmail,
                        name = settings.SenderDisplayName,
                    },
                },
            },
            saveToSentItems = false,
        };

        return JsonSerializer.Serialize(payload);
    }
}
