namespace PortalSaas.Data.Entities;

/// <summary>
/// Un servidor de base de datos físico (HANA / SQL Server / Postgres) -- pertenece a una
/// Organization. De él cuelgan las Company SAP (Company.InstanceId + DatabaseName, solo
/// motores SAP) y las bases externas de los módulos (CompanyModuleConnection.InstanceId +
/// DatabaseName, cualquier motor). Usuario/clave técnicos son los por defecto; un módulo
/// puede sobreescribirlos (ver CompanyModuleConnection).
/// </summary>
public sealed class Instance
{
    public long Id { get; set; }

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string Host { get; set; } = null!;
    public int Port { get; set; } = 30015;

    /// <summary>"hana" | "sqlserver" -- ver InstanceEngineType.</summary>
    public string EngineType { get; set; } = null!;

    public string TechnicalUsername { get; set; } = null!;

    /// <summary>Cifrado AES-256-GCM antes de guardar -- ver PortalSAP_v2 ISecretoCifradoService, mismo criterio acá.</summary>
    public string TechnicalSecretKey { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public ICollection<Company> Companies { get; set; } = new List<Company>();
}

/// <summary>
/// Motor de una Instance. Mismos strings que ExternalDatabaseEngineType (Abstractions) --
/// el resolver de bases externas los pasa tal cual al plugin.
/// </summary>
public static class InstanceEngineType
{
    public const string Hana = "hana";
    public const string SqlServer = "sqlserver";
    public const string Postgres = "postgres";

    public static readonly IReadOnlyCollection<string> All = [Hana, SqlServer, Postgres];

    /// <summary>Motores sobre los que puede correr una compañía SAP B1 (Company.InstanceId).</summary>
    public static readonly IReadOnlyCollection<string> Sap = [Hana, SqlServer];

    public static bool IsSap(string engineType) => Sap.Contains(engineType);
}
