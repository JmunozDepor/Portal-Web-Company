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

    /// <summary>
    /// URL pública donde corre la PWA de captura -- la misma para todas las
    /// sucursales/equipos de esta compañía. Se usa para generar el QR de
    /// "Conectar equipo" (Pages/ConfiguracionCaptura) así un equipo nuevo entra
    /// escaneándolo en vez de tipear la URL a mano en el teclado del lector.
    /// </summary>
    public string? PwaUrl { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
