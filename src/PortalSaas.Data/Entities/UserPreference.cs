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

    /// <summary>"claro" | "oscuro" | "teal" | "violeta" -- ver UserThemePreference.</summary>
    public string Theme { get; set; } = UserThemePreference.Claro;

    public bool EmailNotificationsEnabled { get; set; } = true;

    /// <summary>
    /// Compañía que SelectCompany preselecciona/auto-continúa sin pedir elegir de nuevo.
    /// Nullable: sin default, el usuario elige como siempre. No es una FK dura contra
    /// borrado en cascada -- si la compañía se desactiva o borra, SelectCompany la valida
    /// igual que cualquier otra (Company.IsActive) y si no pasa, vuelve a pedir elegir.
    /// </summary>
    public Guid? DefaultCompanyId { get; set; }
}

/// <summary>
/// REVISADO (2026-08-19, consolidación de preferencias): antes tenía 3 valores
/// (light/dark/system) que ningún lado de la UI aplicaba de verdad -- el tema
/// que SÍ se veía en pantalla lo controlaba un selector aparte en el topbar
/// (theme-switcher.js, 4 temas reales vía localStorage, nunca sincronizado con
/// esto). Se unificó a los 4 temas reales que site.css/sidebar.css ya
/// implementan (data-theme="claro|oscuro|teal|violeta") -- este es AHORA el
/// único mecanismo, aplicado server-side en _LayoutMaestro.cshtml para
/// cualquier sesión autenticada (anclado a la cuenta, no al navegador).
/// </summary>
public static class UserThemePreference
{
    public const string Claro = "claro";
    public const string Oscuro = "oscuro";
    public const string Teal = "teal";
    public const string Violeta = "violeta";

    public static readonly IReadOnlyCollection<string> All = [Claro, Oscuro, Teal, Violeta];
}
