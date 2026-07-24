using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Correo;

/// <summary>
/// Implementación real de IEmailSenderService -- resuelve `email_settings` de la
/// organización y despacha al proveedor configurado (Google Workspace o Microsoft
/// 365, ver docs/06-...md §7), sin que el llamador necesite saber cuál es.
/// </summary>
public sealed class EmailSenderService : IEmailSenderService
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secrets;
    private readonly IHttpClientFactory _httpClientFactory;

    public EmailSenderService(PortalSaasDbContext db, ISecretoCifradoService secrets, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _secrets = secrets;
        _httpClientFactory = httpClientFactory;
    }

    public async Task SendAsync(Guid organizationId, EmailMessage message, CancellationToken ct = default)
    {
        var settings = await _db.EmailSettings
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId && s.IsActive, ct)
            ?? throw new InvalidOperationException(
                $"La organización '{organizationId}' no tiene un proveedor de correo configurado (o está inactivo) -- dar de alta en Administración > Correo antes de poder enviar.");

        var decryptedConfig = _secrets.Decrypt(settings.EncryptedProviderConfig);
        var httpClient = _httpClientFactory.CreateClient(nameof(EmailSenderService));

        switch (settings.Provider)
        {
            case EmailProviderType.Microsoft365:
                var microsoftConfig = JsonSerializer.Deserialize<Microsoft365ProviderConfig>(decryptedConfig)
                    ?? throw new InvalidOperationException("Configuración de Microsoft 365 inválida o corrupta.");
                await new Microsoft365EmailSender(httpClient).SendAsync(settings, microsoftConfig, message, ct);
                break;

            case EmailProviderType.GoogleWorkspace:
                var googleConfig = JsonSerializer.Deserialize<GoogleWorkspaceProviderConfig>(decryptedConfig)
                    ?? throw new InvalidOperationException("Configuración de Google Workspace inválida o corrupta.");
                await new GoogleWorkspaceEmailSender(httpClient).SendAsync(settings, googleConfig, message, ct);
                break;

            default:
                throw new InvalidOperationException($"Proveedor de correo '{settings.Provider}' no reconocido.");
        }
    }
}
