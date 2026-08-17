namespace Modulo.Wms.Models;

/// <summary>
/// Reconciliación del resultado real de un documento enviado a Oracle WMS Cloud --
/// init_stage_interface (WmsCloudConnector) solo confirma recepción, no validación de
/// negocio. Espejo de STG_WMS_VALIDATION del legado (WMS_Suite), con company_id real
/// en vez de aislamiento por schema. Ver
/// docs/superpowers/specs/2026-08-17-batching-y-reconciliacion-wms-design.md.
/// </summary>
public class WmsExportValidation
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TipoDoc { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
    public DateTimeOffset EnviadoEn { get; set; } = DateTimeOffset.UtcNow;
    public int? WmsStatusId { get; set; }
    public string? WmsStatusDesc { get; set; }
    public string? WmsErrorMsg { get; set; }
    public DateTimeOffset? ValidadoEn { get; set; }
    public int Intentos { get; set; }
}
