namespace PortalSaas.Data.Entities;

/// <summary>Un servidor HANA/SQL Server físico -- pertenece a una Organization, varias Company de esa misma Organization pueden compartirlo.</summary>
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

public static class InstanceEngineType
{
    public const string Hana = "hana";
    public const string SqlServer = "sqlserver";

    public static readonly IReadOnlyCollection<string> All = [Hana, SqlServer];
}
