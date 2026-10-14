using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Host.Notificaciones;

/// <summary>Mapea NotificationDto.Severity al modificador real de .badge-status (site.css) -- compartido por el dropdown del topbar y /notificaciones.</summary>
public static class NotificationSeverityDisplay
{
    public static string BadgeModifier(string severity) => severity switch
    {
        NotificationSeverity.Success => "success",
        NotificationSeverity.Warning => "warning",
        NotificationSeverity.Danger => "danger",
        _ => "neutral",
    };
}
