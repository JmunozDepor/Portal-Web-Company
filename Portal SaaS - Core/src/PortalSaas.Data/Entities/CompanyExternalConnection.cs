namespace PortalSaas.Data.Entities;

public sealed class CompanyExternalConnection
{
    public long Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Único por compañía.</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>ExternalConnectionType.* ("db_postgres" | "db_sqlserver" | "db_hana" | "http_api").</summary>
    public string Tipo { get; set; } = null!;

    public string? Host { get; set; }
    public string? BaseUrl { get; set; }
    public int? Port { get; set; }
    public string? DatabaseName { get; set; }
    public string? TechnicalUsername { get; set; }

    /// <summary>Cifrado con ISecretoCifradoService. Write-only.</summary>
    public string? TechnicalSecretKey { get; set; }

    /// <summary>JSON con parámetros específicos del tipo/módulo.</summary>
    public string? ConfiguracionExtra { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<CompanyModuleConnection> ModuleBindings { get; set; } = new List<CompanyModuleConnection>();
}
