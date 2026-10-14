using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Notificaciones;

/// <summary>Implementación real de INotificationService.</summary>
public sealed class NotificationService : INotificationService
{
    // Largos de columna de notifications (ver PortalSaasDbContext) -- TryEnqueueAsync recorta a esto.
    private const int TitleMaxLength = 200;
    private const int MessageMaxLength = 2000;

    private readonly PortalSaasDbContext _db;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(PortalSaasDbContext db, ILogger<NotificationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Guid> EnqueueAsync(
        Guid userId,
        string title,
        string message,
        string severity = NotificationSeverity.Info,
        string? url = null,
        CancellationToken ct = default)
    {
        var notification = new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Severity = severity,
            Url = url,
        };

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);
        return notification.Id;
    }

    public async Task<Guid?> TryEnqueueAsync(
        Guid userId,
        string title,
        string message,
        string severity = NotificationSeverity.Info,
        string? url = null,
        CancellationToken ct = default)
    {
        var notification = new Notification
        {
            UserId = userId,
            Title = Truncate(title, TitleMaxLength),
            Message = Truncate(message, MessageMaxLength),
            Severity = severity,
            Url = url,
        };

        try
        {
            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync(ct);
            return notification.Id;
        }
        catch (Exception ex)
        {
            // Sacarla del tracker: si no, el próximo SaveChanges de este mismo scope
            // (cualquier otra cosa del request) reintentaría el INSERT fallido.
            _db.Entry(notification).State = EntityState.Detached;
            _logger.LogWarning(ex, "No se pudo dejar la notificación \"{Title}\" para el usuario {UserId}.", notification.Title, userId);
            return null;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    public async Task<IReadOnlyList<NotificationDto>> ListRecentAsync(Guid userId, int limit = 20, CancellationToken ct = default) =>
        await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new NotificationDto(n.Id, n.Title, n.Message, n.Severity, n.Url, n.IsRead, n.CreatedAt))
            .ToListAsync(ct);

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default) =>
        _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

    public async Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct);

        if (notification is null || notification.IsRead)
        {
            return;
        }

        notification.IsRead = true;
        notification.ReadAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default)
    {
        var unread = await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync(ct);
        if (unread.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }
}
