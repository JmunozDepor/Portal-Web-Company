using System.ComponentModel.DataAnnotations;

namespace PortalSaas.Abstractions.Modelos;

/// <summary>Instancia (servidor de BD) de la organización, elegible para la base de un módulo. NUNCA incluye el secreto.</summary>
public sealed record DatabaseInstanceOptionDto(long Id, string Name, string EngineType, string Host, int Port);

/// <summary>
/// Una fila de la grilla módulo → (instancia, base) de una compañía. Solo módulos con
/// IModuloPortal.RequiresExternalDatabase. InstanceId null = sin asignar. NUNCA incluye el secreto.
/// </summary>
public sealed record ModuleConnectionBindingDto(
    string ModuleCode,
    string ModuleName,
    long? InstanceId,
    string? InstanceName,
    string? DatabaseName,
    string? OwnTechnicalUsername,
    bool HasOwnCredentials);

/// <summary>
/// Alta/edición de la base de un módulo. UseOwnCredentials=false ⇒ usa usuario/clave de la
/// instancia (y borra cualquier credencial propia guardada). UseOwnCredentials=true ⇒
/// usuario obligatorio; clave obligatoria solo si no había una propia guardada (en blanco =
/// conservar). Secreto write-only.
/// </summary>
public sealed class ModuleDatabaseBindingInput
{
    [Range(1, long.MaxValue, ErrorMessage = "Elige una instancia.")]
    public long InstanceId { get; set; }

    [Required(ErrorMessage = "Ingresa la base de datos.")]
    [StringLength(100)]
    public string DatabaseName { get; set; } = string.Empty;

    public bool UseOwnCredentials { get; set; }

    [StringLength(100)]
    public string? TechnicalUsername { get; set; }

    public string? TechnicalSecretKey { get; set; }
}

public sealed record ConnectionTestResultDto(bool Ok, string? Error, long? ElapsedMs);
