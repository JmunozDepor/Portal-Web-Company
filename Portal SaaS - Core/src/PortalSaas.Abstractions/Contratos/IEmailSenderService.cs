using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Envía correo en nombre de una organización -- oculta al llamador si el proveedor
/// configurado es Google Workspace o Microsoft 365 (ver docs/06-...md §7), mismo
/// criterio que `HanaService` de PortalSAP_v2 oculta si el motor activo es HANA o
/// SQL Server. Nuevo en este proyecto.
///
/// Falla cerrado: si la organización no tiene `email_settings` activa, lanza
/// `InvalidOperationException` en vez de fallar en silencio -- mismo criterio de
/// "falla hacia lo más estricto" que el resto del proyecto (ver CLAUDE.md).
/// </summary>
public interface IEmailSenderService
{
    Task SendAsync(Guid organizationId, EmailMessage message, CancellationToken ct = default);
}
