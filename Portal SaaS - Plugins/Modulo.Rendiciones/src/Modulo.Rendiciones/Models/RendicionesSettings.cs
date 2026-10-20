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

    public bool SapCatalogSyncEnabled { get; set; } = true;

    /// <summary>
    /// Si es false (default), los montos se muestran redondeados sin decimales (ej.
    /// "297.024") -- la mayoría de las empresas chilenas no factura con centavos de CLP.
    /// Configurable por compañía en Configuración &gt; Formato de Montos
    /// (ver IAmountFormatService, que es quien realmente aplica esto en pantalla).
    /// </summary>
    public bool ShowAmountDecimals { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
