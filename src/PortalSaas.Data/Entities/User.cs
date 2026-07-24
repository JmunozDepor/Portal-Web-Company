namespace PortalSaas.Data.Entities;

/// <summary>
/// Usuario del portal -- pertenece a una Organization (no solo "existe" como en
/// PortalSAP_v2). La cuenta SIEMPRE está asociada a un correo (Email es obligatorio,
/// no nullable como en PortalSAP_v2 -- decisión explícita: el correo es el canal de
/// recuperación de clave y, a futuro, de notificaciones, así que no puede faltar).
/// Username y Email son únicos DENTRO de la organización, no global.
/// </summary>
public sealed class User
{
    // Generado en C#, no en la base -- ver Organization.Id.
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public string Username { get; set; } = null!;

    /// <summary>Obligatorio -- toda cuenta queda siempre asociada a un correo real.</summary>
    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;
    public string PasswordSalt { get; set; } = null!;

    public bool IsAdmin { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsEmailConfirmed { get; set; }
    public bool HasTwoFactorEnabled { get; set; }
    public string? TwoFactorMethod { get; set; }
    public string? TwoFactorSecret { get; set; }

    public UserPreference? Preference { get; set; }
}
