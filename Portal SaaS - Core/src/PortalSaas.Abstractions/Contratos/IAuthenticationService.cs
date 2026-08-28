using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Valida credenciales de un usuario DENTRO de una organización (el login siempre
/// resuelve primero a qué organización pertenece la cuenta, mismo criterio que
/// PortalSAP_v2 con la empresa). Nuevo en este proyecto -- PortalSAP_v2 tenía el
/// modelo de datos listo pero nunca implementó el flujo real.
///
/// Política de bloqueo: una cuenta ya bloqueada (`IsLocked`) o inactiva
/// (`IsActive = false`) se rechaza SIN evaluar la contraseña. Un intento fallido
/// incrementa `FailedLoginAttempts`; al llegar al umbral, la cuenta se bloquea sola
/// (ver AuthenticationService.MaxFailedAttempts). Un login exitoso resetea el
/// contador y actualiza `LastLoginAt`.
/// </summary>
public interface IAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(Guid organizationId, string emailOrUsername, string password, CancellationToken ct = default);
}
