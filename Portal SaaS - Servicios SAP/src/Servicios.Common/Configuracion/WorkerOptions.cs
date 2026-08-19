namespace Servicios.Common.Configuracion;

/// <summary>
/// Bindea la sección "Worker" de appsettings.json de cada servicio. Reemplaza el
/// "TimeSpan.FromMinutes(5)" hardcodeado del servicio legado -- el intervalo del ciclo
/// principal queda como dato de configuración, no una constante de código, para poder
/// ajustarlo sin recompilar.
/// </summary>
public sealed class WorkerOptions
{
    public const string SeccionConfiguracion = "Worker";

    /// <summary>Cada cuántos segundos se repite el ciclo completo (todas las compañías activas).</summary>
    public int CicloIntervaloSegundos { get; set; } = 300;

    public TimeSpan CicloIntervalo => TimeSpan.FromSeconds(CicloIntervaloSegundos);
}
