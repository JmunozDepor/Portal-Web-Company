namespace Modulo.AuditoriaInventario.Models;

/// <summary>
/// Credencial liviana para el equipo de captura (PWA mobile) -- deliberadamente
/// NO reutiliza el sistema de usuarios/menús/permisos del portal
/// (ICurrentUserContext es de sesión de portal, no aplica a la API de la PWA). Un
/// capturador de campo no necesita cuenta de portal, solo login contra
/// Api/V1/AuthController.
/// </summary>
public class CaptureUser
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required string Username { get; set; }

    public required string PasswordHash { get; set; }

    public string? FullName { get; set; }

    public bool IsActive { get; set; } = true;
}
