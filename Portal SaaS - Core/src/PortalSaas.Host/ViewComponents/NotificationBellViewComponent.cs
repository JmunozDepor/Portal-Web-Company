using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Host.ViewComponents;

/// <summary>
/// Campana de notificaciones del topbar (Pages/Shared/_LayoutMaestro.cshtml) --
/// server-rendered en cada carga de página, mismo criterio que CompanySwitcher (sin
/// polling/fetch todavía, ver INotificationService).
/// </summary>
public sealed class NotificationBellViewComponent : ViewComponent
{
    private const int MaxItemsEnDropdown = 20;

    private readonly INotificationService _notifications;
    private readonly ICurrentUserContext _currentUser;

    public NotificationBellViewComponent(INotificationService notifications, ICurrentUserContext currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var items = await _notifications.ListRecentAsync(_currentUser.UserId, MaxItemsEnDropdown);
        var unreadCount = items.Count(i => !i.IsRead);
        return View(new NotificationBellViewModel(items, unreadCount));
    }
}

public sealed record NotificationBellViewModel(IReadOnlyList<NotificationDto> Items, int UnreadCount);
