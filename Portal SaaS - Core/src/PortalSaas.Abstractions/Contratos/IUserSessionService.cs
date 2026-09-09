using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// "Clientes conectados" -- sesiones de portal activas por login exitoso de tenant
/// (Account/Login.cshtml.cs). Permite al administrador de plataforma ver quién está
/// logueado ahora mismo, global y por organización, y forzar el cierre de una sesión
/// puntual (RevokeAsync) -- la cookie de ese usuario deja de ser válida en su próxima
/// request (ver CookieAuthenticationEvents.OnValidatePrincipal en Program.cs).
/// </summary>
public interface IUserSessionService
{
    /// <summary>
    /// Crea la sesión al loguearse y devuelve el token EN TEXTO PLANO -- se guarda como
    /// claim en la cookie (nunca en la base, ver UserSession.TokenHash).
    /// </summary>
    Task<string> CreateAsync(Guid userId, Guid organizationId, string? ipAddress, string? userAgent, CancellationToken ct = default);

    /// <summary>Fija la compañía SAP activa de la sesión -- llamado desde SelectCompany.cshtml.cs, mismo momento en que se agrega el claim "CompanyId".</summary>
    Task SetCompanyAsync(string rawToken, Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Valida la cookie en cada request y, si la sesión sigue activa, refresca su
    /// marca de última actividad (LastSeenAt) -- con un límite para no escribir en
    /// cada request. Devuelve <see cref="SessionValidationStatus.Revoked"/> solo
    /// cuando la fila existe y fue revocada a propósito; una fila ausente devuelve
    /// <see cref="SessionValidationStatus.NotFound"/> y NO debe cerrar la cookie.
    /// </summary>
    Task<SessionValidationStatus> ValidateAndTouchAsync(string rawToken, CancellationToken ct = default);

    /// <summary>Listado para el backoffice -- organizationId null trae todas las organizaciones (vista global).</summary>
    Task<IReadOnlyList<UserSessionDto>> ListActiveAsync(Guid? organizationId = null, CancellationToken ct = default);

    /// <summary>Cierra una sesión puntual a la fuerza. No falla si sessionId no existe o ya estaba revocada (idempotente).</summary>
    Task RevokeAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Cierre normal de sesión (Logout) -- revoca por el token de la propia cookie, no por id.</summary>
    Task RevokeByTokenAsync(string rawToken, CancellationToken ct = default);
}
