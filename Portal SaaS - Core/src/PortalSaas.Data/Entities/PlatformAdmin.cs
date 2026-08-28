namespace PortalSaas.Data.Entities;

/// <summary>
/// Administrador de la plataforma -- NO pertenece a ninguna Organization, la
/// administra todas (crear organizaciones, activarlas/suspenderlas). Actor distinto
/// de <see cref="User"/> (empleado de una organización cliente); no reabre la
/// decisión de "1 usuario = 1 organización" de docs/03-MODELO-CORE-COMERCIAL.md §1,
/// que aplica solo a usuarios dentro de una organización. Por no tener
/// OrganizationId que lo scope, Email es único GLOBAL (a diferencia de User.Email).
/// </summary>
public sealed class PlatformAdmin
{
    // Generado en C#, no en la base -- ver Organization.Id.
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;
    public string PasswordSalt { get; set; } = null!;

    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
