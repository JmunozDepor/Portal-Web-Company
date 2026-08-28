namespace PortalSaas.Abstractions.Modelos;

/// <summary>Correo a enviar -- ver IEmailSenderService. Siempre HTML, sin adjuntos por ahora.</summary>
public sealed record EmailMessage(string ToEmail, string Subject, string HtmlBody);
