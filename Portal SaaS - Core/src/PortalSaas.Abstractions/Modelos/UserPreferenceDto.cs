namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Preferencias personales del usuario -- ver IUserPreferenceService. DTO, no la
/// entidad de datos (Abstractions no depende de PortalSaas.Data).
/// </summary>
public sealed record UserPreferenceDto(string Locale, string Timezone, string Theme, bool EmailNotificationsEnabled);
