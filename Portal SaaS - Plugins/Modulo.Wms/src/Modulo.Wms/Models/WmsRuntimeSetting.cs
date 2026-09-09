namespace Modulo.Wms.Models;

/// <summary>
/// Ajuste de motor a nivel de ENTORNO (una fila por BD de módulo, sin company_id):
/// umbrales de reintento de los background services (WmsExistsReconciler,
/// WmsStageErrorReconciler, WmsSlsh/SvshStageParser) que antes eran constantes en
/// código. Se editan sin redeploy desde <c>/wms/ajustes-motor</c>. Si no hay fila
/// para una clave, el servicio usa el default de código (ver
/// <see cref="Services.WmsRuntimeSettingsKeys"/>).
/// </summary>
public sealed class WmsRuntimeSetting
{
    public long Id { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; set; }
}
