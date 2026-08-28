using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Segmento de preferencias personales del usuario (idioma, zona horaria, tema,
/// notificaciones) -- nuevo en este proyecto, no existía en PortalSAP_v2. Separado a
/// propósito de la identidad/seguridad del usuario (ver Entities/UserPreference.cs).
/// </summary>
public interface IUserPreferenceService
{
    /// <summary>Si el usuario todavía no tiene fila de preferencias, la crea con los defaults y la devuelve.</summary>
    Task<UserPreferenceDto> GetOrCreateDefaultAsync(Guid userId, CancellationToken ct = default);

    Task UpdateAsync(Guid userId, UserPreferenceDto preferences, CancellationToken ct = default);
}
