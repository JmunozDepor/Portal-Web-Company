namespace Modulo.AuditoriaInventario.Models;

/// <summary>
/// Formatos de código de barra que la PWA acepta al escanear -- configurado por el
/// administrador (Pages/ConfiguracionCaptura), no por el capturador en el equipo (a
/// diferencia de "Validar producto", que sí es un ajuste por dispositivo). Una fila
/// por compañía; si no existe fila, GetAjustesCapturaAsync devuelve los tres formatos
/// habilitados por defecto (mismo criterio permisivo que el resto del maestro: un
/// equipo recién aprovisionado no debe quedar bloqueando escaneos por falta de
/// configuración explícita).
/// </summary>
public class CaptureSettings
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public bool AllowEan8 { get; set; } = true;

    public bool AllowUpcA { get; set; } = true;

    public bool AllowEan13 { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
