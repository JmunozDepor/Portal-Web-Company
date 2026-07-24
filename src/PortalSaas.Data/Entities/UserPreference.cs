namespace PortalSaas.Data.Entities;

/// <summary>
/// Preferencias personales del usuario -- 1:1 con User, separado de la tabla de
/// identidad/seguridad a propósito (son datos de personalización, no de
/// autenticación; separar evita que un cambio de preferencia toque la fila
/// crítica de credenciales). Nueva en este proyecto, no existía en PortalSAP_v2.
/// </summary>
public sealed class UserPreference
{
    /// <summary>Comparte PK con User (relación 1:1) -- no es un id propio.</summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Ej. "es-CL", "en-US".</summary>
    public string Locale { get; set; } = "es-CL";

    /// <summary>Ej. "America/Santiago" (IANA time zone).</summary>
    public string Timezone { get; set; } = "America/Santiago";

    /// <summary>"light" | "dark" | "system" -- ver UserThemePreference.</summary>
    public string Theme { get; set; } = UserThemePreference.System;

    public bool EmailNotificationsEnabled { get; set; } = true;
}

public static class UserThemePreference
{
    public const string Light = "light";
    public const string Dark = "dark";
    public const string System = "system";

    public static readonly IReadOnlyCollection<string> All = [Light, Dark, System];
}
