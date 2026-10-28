using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.FacturasProveedor.Tests.Fakes;

public sealed class FakeNotificationService : INotificationService
{
    public List<(Guid UserId, string Title, string Message, string Severity, string? Url)> Enqueued { get; } = new();

    public Task<Guid> EnqueueAsync(Guid userId, string title, string message, string severity = NotificationSeverity.Info,
        string? url = null, CancellationToken ct = default)
    {
        Enqueued.Add((userId, title, message, severity, url));
        return Task.FromResult(Guid.NewGuid());
    }

    public async Task<Guid?> TryEnqueueAsync(Guid userId, string title, string message, string severity = NotificationSeverity.Info,
        string? url = null, CancellationToken ct = default) =>
        await EnqueueAsync(userId, title, message, severity, url, ct);

    public Task<IReadOnlyList<NotificationDto>> ListRecentAsync(Guid userId, int limit = 20, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
}
