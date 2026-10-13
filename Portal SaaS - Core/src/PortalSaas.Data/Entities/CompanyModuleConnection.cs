namespace PortalSaas.Data.Entities;

/// <summary>
/// Base de datos externa de un módulo para una compañía -- relación 1 a 1
/// (CompanyId, ModuleCode). Mismo patrón que Company (InstanceId + DatabaseName): el
/// servidor y su credencial por defecto viven en Instance; acá solo la base y,
/// opcionalmente, una credencial propia (mínimo privilegio: el usuario de un módulo no
/// necesita ver las bases de los demás). TechnicalUsername/TechnicalSecretKey van los dos
/// o ninguno -- null = usa los de la instancia (ver ModuleDatabaseCredentials).
/// </summary>
public sealed class CompanyModuleConnection
{
    public long Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>IModuloPortal.ModuleCode.</summary>
    public string ModuleCode { get; set; } = null!;

    public long InstanceId { get; set; }
    public Instance Instance { get; set; } = null!;

    public string DatabaseName { get; set; } = null!;

    /// <summary>null = usa Instance.TechnicalUsername.</summary>
    public string? TechnicalUsername { get; set; }

    /// <summary>Cifrado con ISecretoCifradoService. Write-only. null = usa Instance.TechnicalSecretKey.</summary>
    public string? TechnicalSecretKey { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
