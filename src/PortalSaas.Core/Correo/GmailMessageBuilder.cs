using System.Text;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Correo;

/// <summary>
/// Arma el mensaje MIME (RFC 2822) codificado en base64url que exige
/// `POST /users/{senderEmail}/messages/send` de la Gmail API (campo `raw`) --
/// separado en un método puro (sin HTTP) para poder testearlo sin red ni
/// credenciales reales. Referencia: https://developers.google.com/gmail/api/guides/sending
/// </summary>
internal static class GmailMessageBuilder
{
    public static string BuildRawMimeMessageBase64Url(EmailSettings settings, EmailMessage message)
    {
        var from = settings.SenderDisplayName is null
            ? settings.SenderEmail
            : $"{settings.SenderDisplayName} <{settings.SenderEmail}>";

        var mime = $"From: {from}\r\n" +
                   $"To: {message.ToEmail}\r\n" +
                   $"Subject: {EncodeSubject(message.Subject)}\r\n" +
                   "MIME-Version: 1.0\r\n" +
                   "Content-Type: text/html; charset=UTF-8\r\n" +
                   "\r\n" +
                   message.HtmlBody;

        return Base64UrlEncode(Encoding.UTF8.GetBytes(mime));
    }

    /// <summary>
    /// RFC 2047 "encoded-word" (B-encoding) -- necesario porque el asunto puede traer
    /// acentos/ñ y las cabeceras de correo son estrictamente ASCII.
    /// </summary>
    private static string EncodeSubject(string subject) =>
        $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(subject))}?=";

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
