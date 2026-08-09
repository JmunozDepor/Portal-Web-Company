namespace Modulo.Wms.Models;

/// <summary>
/// Configuración operativa de WmsSapIntegration.Service, editable desde este plugin
/// sin redeploy del servicio -- portado de INT_SERVICE_CONFIG (WMS_Suite). Solo
/// tiene efecto si esa instancia tiene "ConfigSource": "Database" en su propio
/// appsettings.json y apunta a esta tabla (ver ARQUITECTURA.md, Fase 1: el servicio
/// deja de leer su schema HANA local y lee acá).
///
/// Whitelist de ConfigKey soportadas -- mismo criterio que la tabla original:
/// deliberadamente NUNCA credenciales (esas quedan en env/vault del servicio).
/// </summary>
public sealed class WmsServiceConfig
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ConfigKey { get; set; } = null!;
    public string? ConfigValue { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; set; }
}
