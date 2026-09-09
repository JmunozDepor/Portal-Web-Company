namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de "clientes conectados" para el backoffice de plataforma -- ver IUserSessionService.ListActiveAsync.</summary>
public sealed record UserSessionDto(
    Guid SessionId,
    Guid OrganizationId,
    string OrganizationName,
    Guid UserId,
    string Username,
    string Email,
    Guid? CompanyId,
    string? CompanyName,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);

/// <summary>
/// Resultado de validar la cookie de una sesión de portal en cada request
/// (ver IUserSessionService.ValidateAndTouchAsync / OnValidatePrincipal en Program.cs).
/// </summary>
public enum SessionValidationStatus
{
    /// <summary>La sesión existe y no fue revocada -- la cookie sigue siendo válida.</summary>
    Active,

    /// <summary>La sesión fue revocada a propósito (botón "Desconectar" del backoffice, o logout) -- hay que cerrar la cookie.</summary>
    Revoked,

    /// <summary>
    /// No hay fila para ese token (purgada por la limpieza de sesiones muertas, base
    /// recreada, o token inválido). NO se cierra la cookie por esto: si sigue siendo
    /// criptográficamente válida y no expiró, se deja pasar -- lo contrario solo
    /// provocaba un rebote mudo al login imposible de diagnosticar.
    /// </summary>
    NotFound,
}
