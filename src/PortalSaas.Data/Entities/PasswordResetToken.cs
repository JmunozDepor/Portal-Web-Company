namespace PortalSaas.Data.Entities;

/// <summary>
/// Token de recuperación de contraseña por correo -- portado del "gancho" de
/// PortalSAP_v2 (`TOKEN_RECUPERACION`, nunca implementado ahí), ahora con el servicio
/// real detrás (ver PortalSaas.Core.Seguridad.PasswordResetService). Solo se guarda el
/// HASH del token, nunca el valor real -- el valor real solo existe en el correo que
/// recibe el usuario y en memoria durante la request que lo genera/valida.
/// </summary>
public sealed class PasswordResetToken
{
    public long Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
