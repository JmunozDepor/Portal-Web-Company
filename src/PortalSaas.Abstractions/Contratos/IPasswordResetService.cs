namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Recuperación de contraseña por correo -- nuevo en este proyecto (en PortalSAP_v2
/// era solo un "gancho" de modelo de datos, `TOKEN_RECUPERACION`, nunca implementado).
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// Genera un token de recuperación si existe un usuario activo con ese correo en
    /// la organización dada. Devuelve el token EN TEXTO PLANO (para enviarlo por
    /// correo -- nunca se guarda así, ver PasswordResetToken.TokenHash) o `null` si no
    /// hay un usuario que lo amerite.
    ///
    /// Deliberado: el llamador (futuro Host/API) debe mostrar el MISMO mensaje
    /// genérico ("si el correo existe, se envió un link") sin importar el resultado
    /// real -- devolver `null` acá no es "usuario no encontrado" de cara al usuario
    /// final, es información interna para decidir si de verdad se envía el correo.
    /// </summary>
    Task<string?> RequestResetAsync(Guid organizationId, string email, CancellationToken ct = default);

    /// <summary>
    /// Aplica la nueva contraseña si el token es válido, no expiró y no fue usado
    /// antes. Devuelve false en cualquiera de esos casos -- sin distinguir el motivo
    /// exacto de cara al usuario (mismo criterio anti-enumeración).
    /// </summary>
    Task<bool> ResetPasswordAsync(string rawToken, string newPassword, CancellationToken ct = default);
}
