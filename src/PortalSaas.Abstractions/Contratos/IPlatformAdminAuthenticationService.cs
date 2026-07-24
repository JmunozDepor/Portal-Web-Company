using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Valida credenciales de un administrador de plataforma -- a diferencia de
/// IAuthenticationService, no resuelve ninguna organización primero porque el
/// administrador de plataforma no pertenece a ninguna (ver Entities/PlatformAdmin.cs).
/// Misma política de bloqueo que IAuthenticationService (ver esa interfaz para el
/// detalle): cuenta bloqueada o inactiva se rechaza sin evaluar la contraseña, un
/// intento fallido incrementa el contador, al llegar al umbral se bloquea sola.
/// </summary>
public interface IPlatformAdminAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(string email, string password, CancellationToken ct = default);
}
