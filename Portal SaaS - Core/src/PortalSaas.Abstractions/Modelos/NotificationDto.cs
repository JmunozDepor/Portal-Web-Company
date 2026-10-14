namespace PortalSaas.Abstractions.Modelos;

/// <summary>Notificación de un usuario -- ver INotificationService. DTO, no la entidad de datos.</summary>
public sealed record NotificationDto(
    Guid Id,
    string Title,
    string Message,
    string Severity,
    string? Url,
    bool IsRead,
    DateTimeOffset CreatedAt);

/// <summary>Valores válidos de NotificationDto.Severity -- mismo catálogo que el check constraint de la tabla.</summary>
public static class NotificationSeverity
{
    public const string Info = "info";
    public const string Success = "success";
    public const string Warning = "warning";
    public const string Danger = "danger";

    public static readonly IReadOnlyCollection<string> All = [Info, Success, Warning, Danger];
}
