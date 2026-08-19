namespace Servicios.TransferenciaAutomatica_v2.Domain;

/// <summary>Bindea la sección "AsignacionParcial" de appsettings.json de este servicio.</summary>
public sealed class AsignacionParcialOptions
{
    public const string SeccionConfiguracion = "AsignacionParcial";

    /// <summary>
    /// Cuántos ciclos consecutivos con asignación parcial (stock insuficiente en la
    /// cascada de bodegas origen) se toleran antes de dejar de reintentar un documento y
    /// marcarlo completado igual, con lo que ya se pudo transferir. Evita que un pedido
    /// cuyo faltante nunca se puede cubrir quede reintentándose para siempre sin que nadie
    /// se entere.
    /// </summary>
    public int MaxIntentos { get; set; } = 3;

    /// <summary>
    /// Carpeta donde se persiste el contador de intentos por documento (un archivo JSON
    /// por compañía) -- sobrevive a un reinicio del Windows Service. Relativa o absoluta,
    /// mismo criterio que Logging:Folder.
    /// </summary>
    public string Folder { get; set; } = "estado-asignacion";
}
