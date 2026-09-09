namespace PortalSaas.Data.Entities;

/// <summary>
/// Sesión de portal activa de un usuario de tenant -- una fila por login exitoso
/// (Account/Login.cshtml.cs), permite al administrador de plataforma ver "clientes
/// conectados" (qué usuarios están logueados ahora mismo, global y por organización) y
/// forzar el cierre de una sesión puntual. Solo se guarda el HASH del token (mismo
/// criterio que PasswordResetToken.TokenHash) -- el valor real vive únicamente como
/// claim dentro de la cookie de sesión del usuario, nunca en la base.
///
/// Revocar acá (IsRevoked = true) no invalida la cookie por sí solo -- la valida
/// CookieAuthenticationEvents.OnValidatePrincipal (Program.cs), que en cada request
/// chequea si la sesión fue REVOCADA a propósito y solo entonces fuerza el cierre.
/// Una fila ausente (purgada, base recreada) NO invalida una cookie todavía vigente.
/// </summary>
public sealed class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    // FK directa a Organization (además de la que ya cuelga de User) a propósito, para
    // no tener que hacer join contra Users en cada listado del backoffice -- ver el
    // comentario de DeleteBehavior.Restrict en PortalSaasDbContext (evita el mismo bug
    // de rutas de cascada múltiples ya corregido para Company.Organization).
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    // Compañía SAP activa -- null hasta que el usuario la elige en SelectCompany.cshtml
    // (o para siempre, si la organización no tiene ninguna). Se actualiza en el mismo
    // POST que fija el claim "CompanyId".
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }

    public string TokenHash { get; set; } = null!;

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Última vez que la cookie de esta sesión se validó en un request (ver
    // CookieAuthenticationEvents.OnValidatePrincipal en Program.cs). La cookie usa
    // SlidingExpiration, así que su vida real se cuenta desde la última actividad, no
    // desde el login -- por eso la purga de sesiones muertas y "clientes conectados"
    // se miden contra este campo y no contra CreatedAt (si no, a un usuario activo
    // desde hace más de 8 h se le borraba la fila con la cookie todavía viva y el
    // siguiente request lo echaba al login sin aviso). Se refresca como mucho una vez
    // cada pocos minutos (ver UserSessionService.ValidateAndTouchAsync), no en cada
    // request, para no escribir de más.
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsRevoked { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
