namespace PortalSaas.Data.Entities;

/// <summary>
/// Notificación de un usuario (campana del topbar) -- primera pieza de la
/// infraestructura de Notificaciones (2026-10-14). Resuelve organization_id
/// indirectamente vía User.OrganizationId, mismo criterio que UserHomeShortcut --
/// no hace falta duplicar el id acá.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;

    /// <summary>"info" | "success" | "warning" | "danger" -- ver NotificationSeverity (Abstractions).</summary>
    public string Severity { get; set; } = "info";

    /// <summary>Ruta interna opcional a la que navega el click (ej. "/wms/dashboard"). Nunca una URL externa.</summary>
    public string? Url { get; set; }

    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}
