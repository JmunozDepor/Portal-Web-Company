namespace Modulo.Rendiciones.Models;

/// <summary>
/// Configuración global del recordatorio diario -- fila única (Id siempre 1), no hay
/// concepto de "settings globales de plataforma" reusable en el portal todavía (ver
/// docs/superpowers/specs/2026-08-11-flujo-aprobacion-notificaciones-design.md),
/// así que vive acá, propia del plugin.
/// </summary>
public class RendicionesSettings
{
    public long Id { get; set; }

    public TimeOnly ReminderHour { get; set; } = new(8, 0);

    public bool ReminderEnabled { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
