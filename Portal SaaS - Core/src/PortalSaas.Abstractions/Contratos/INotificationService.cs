using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Bandeja de notificaciones de un usuario (campana del topbar) -- nuevo en este
/// proyecto (2026-10-14). Sin bus de eventos en la plataforma todavía: cualquier
/// módulo (Core o un plugin, vía Abstractions) que quiera avisarle algo a un usuario
/// llama EnqueueAsync directo, no hay publish/subscribe.
/// </summary>
public interface INotificationService
{
    Task<Guid> EnqueueAsync(
        Guid userId,
        string title,
        string message,
        string severity = NotificationSeverity.Info,
        string? url = null,
        CancellationToken ct = default);

    /// <summary>
    /// Variante best-effort de EnqueueAsync para los módulos: nunca tira (loguea y
    /// devuelve null) y recorta title/message al largo de la columna. Es la que se usa
    /// después de un proceso de negocio ya confirmado -- un aviso caído en la campana
    /// no puede convertir en error algo que sí se hizo (mismo criterio que el correo
    /// de Rendiciones).
    /// </summary>
    Task<Guid?> TryEnqueueAsync(
        Guid userId,
        string title,
        string message,
        string severity = NotificationSeverity.Info,
        string? url = null,
        CancellationToken ct = default);

    /// <summary>Más recientes primero, leídas y no leídas mezcladas (el consumidor distingue por IsRead).</summary>
    Task<IReadOnlyList<NotificationDto>> ListRecentAsync(Guid userId, int limit = 20, CancellationToken ct = default);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default);

    /// <summary>No-op si la notificación no existe o no pertenece a userId -- nunca confía en el id recibido de la UI.</summary>
    Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default);
}
