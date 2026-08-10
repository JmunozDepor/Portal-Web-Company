namespace PortalSaas.Data.Entities;

/// <summary>
/// Conexión self-service a una base de datos EXTERNA propia de un plugin -- ajena al
/// SAP de la organización (eso es <see cref="Instance"/>/<see cref="Company"/>) y ajena
/// a la base propia de la plataforma. Resuelve el hueco documentado en CLAUDE.md
/// ("ISqlServerService (bases SQL Server externas no-SAP de un plugin) -- no existe
/// todavía") para el primer consumidor real: Modulo.Rendiciones (repo externo, base
/// propia). Motor dual desde el día uno (postgres/sqlserver, ver
/// ModuleExternalConnectionEngineType) -- a diferencia del original PortalSAP_v2, que
/// solo soportaba SQL Server para estas bases externas.
///
/// CompanyId obligatorio, sin fallback a nivel Organization -- regla dura del proyecto:
/// todo plugin (interno o externo) personaliza su persistencia por Company, nunca por
/// Organization directo (mismo criterio que GenericImportConfig/GenericImportUserField y
/// UserMenuProfile). Antes existía un fallback "fila global de la organización, CompanyId
/// NULL" para módulos sin concepto de compañía -- se eliminó a propósito: Company ya
/// resuelve a Organization (Company.OrganizationId), así que no hay ningún caso real que
/// justifique una excepción a este esquema.
/// </summary>
public sealed class ModuleExternalConnection
{
    public long Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>SIEMPRE IModuloPortal.ModuleCode del plugin que resuelve la conexión, nunca un literal a mano.</summary>
    public string ModuleCode { get; set; } = null!;

    /// <summary>"postgres" | "sqlserver" -- ver ModuleExternalConnectionEngineType.</summary>
    public string EngineType { get; set; } = null!;

    public string Host { get; set; } = null!;
    public int Port { get; set; }
    public string DatabaseName { get; set; } = null!;
    public string TechnicalUsername { get; set; } = null!;

    /// <summary>Cifrado AES-256-GCM antes de guardar (ISecretoCifradoService), mismo criterio que Instance.TechnicalSecretKey/Company.IntegrationSecretKey.</summary>
    public string TechnicalSecretKey { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ModuleExternalConnectionEngineType
{
    public const string Postgres = "postgres";
    public const string SqlServer = "sqlserver";

    public static readonly IReadOnlyCollection<string> All = [Postgres, SqlServer];
}
