namespace Modulo.Rendiciones.Models;

/// <summary>
/// Configuración del recordatorio diario -- una fila por compañía (CompanyId es la PK),
/// ya que ModuleExternalConnection no impide que varias compañías compartan la misma
/// base externa (ver docs/superpowers/specs/2026-08-11-flujo-aprobacion-notificaciones-design.md).
/// </summary>
public class RendicionesSettings
{
    public required Guid CompanyId { get; set; }

    public TimeOnly ReminderHour { get; set; } = new(8, 0);

    public bool ReminderEnabled { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
