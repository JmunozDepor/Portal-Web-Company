using System.Text;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Correo;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

public class GmailMessageBuilderTests
{
    private static string DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    [Fact]
    public void BuildRawMimeMessageBase64Url_ContieneLosEncabezadosYElCuerpo()
    {
        var settings = new EmailSettings { SenderEmail = "no-reply@comercialdepor.cl", SenderDisplayName = "Portal SAP" };
        var message = new EmailMessage("destinatario@cliente.cl", "Asunto simple", "<p>Cuerpo del correo</p>");

        var raw = GmailMessageBuilder.BuildRawMimeMessageBase64Url(settings, message);
        var mime = DecodeBase64Url(raw);

        Assert.Contains("From: Portal SAP <no-reply@comercialdepor.cl>", mime);
        Assert.Contains("To: destinatario@cliente.cl", mime);
        Assert.Contains("Content-Type: text/html; charset=UTF-8", mime);
        Assert.Contains("<p>Cuerpo del correo</p>", mime);
    }

    [Fact]
    public void BuildRawMimeMessageBase64Url_NoContieneCaracteresDeBase64Estandar()
    {
        // Gmail exige base64url (- y _ en vez de + y /, sin relleno "=") -- si esto
        // falla, la API de Gmail rechaza el mensaje con un error de formato.
        var settings = new EmailSettings { SenderEmail = "no-reply@comercialdepor.cl" };
        var message = new EmailMessage("destinatario@cliente.cl", "Asunto", "Cuerpo largo ".PadRight(300, 'x'));

        var raw = GmailMessageBuilder.BuildRawMimeMessageBase64Url(settings, message);

        Assert.DoesNotContain('+', raw);
        Assert.DoesNotContain('/', raw);
        Assert.DoesNotContain('=', raw);
    }
}
