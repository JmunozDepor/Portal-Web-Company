using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Host.Pages.Notificaciones;

/// <summary>Historial completo de notificaciones del usuario -- ver NotificationBellViewComponent para el dropdown del topbar.</summary>
[Authorize]
public sealed class IndexModel : PageModel
{
    private const int MaxItemsListados = 100;

    private readonly INotificationService _notifications;
    private readonly ICurrentUserContext _currentUser;

    public IndexModel(INotificationService notifications, ICurrentUserContext currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    public IReadOnlyList<NotificationDto> Items { get; private set; } = Array.Empty<NotificationDto>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _notifications.ListRecentAsync(_currentUser.UserId, MaxItemsListados, ct);
    }

    public async Task<IActionResult> OnPostMarkAsReadAsync(Guid id, string? returnUrl, CancellationToken ct)
    {
        await _notifications.MarkAsReadAsync(_currentUser.UserId, id, ct);
        return RedirigirA(returnUrl);
    }

    public async Task<IActionResult> OnPostMarkAllAsReadAsync(string? returnUrl, CancellationToken ct)
    {
        await _notifications.MarkAllAsReadAsync(_currentUser.UserId, ct);
        return RedirigirA(returnUrl);
    }

    private IActionResult RedirigirA(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) && returnUrl is not null ? Redirect(returnUrl) : RedirectToPage();
}
