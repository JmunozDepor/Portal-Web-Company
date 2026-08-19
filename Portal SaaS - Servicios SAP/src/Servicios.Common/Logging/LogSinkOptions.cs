namespace Servicios.Common.Logging;

/// <summary>Bindea la sección "Logging" de appsettings.json de cada servicio.</summary>
public sealed class LogSinkOptions
{
    public const string SeccionConfiguracion = "Logging";

    /// <summary>
    /// El servicio legado no tenía log local propio. Acá el log existe pero se puede
    /// desactivar por configuración (sin recompilar) para un despliegue que no lo quiera.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// "File" es la única implementación real hoy. "SqlServer"/"PostgreSql" quedan
    /// documentadas acá como valores válidos a futuro (ver LogSinkFactory), pero lanzan
    /// NotImplementedException explícito si se seleccionan -- nunca fingir un sink que no
    /// existe.
    /// </summary>
    public string LocalSink { get; set; } = "File";

    /// <summary>Carpeta donde FileLogSink escribe los archivos diarios (relativa o absoluta).</summary>
    public string Folder { get; set; } = "logs";

    /// <summary>Días de retención del histórico local -- la purga corre una vez al día.</summary>
    public int RetentionDays { get; set; } = 30;
}
