namespace PortalSaas.Data.Entities;

/// <summary>
/// Configuración de documentos tributarios electrónicos (DTE) de una compañía: proveedor
/// (FACELE, BES, ...), ambiente, credenciales y datos del emisor. Una fila por compañía (regla
/// dura: todo lo que depende del negocio del cliente cuelga de CompanyId, sin fallback). La
/// contraseña va cifrada con ISecretoCifradoService y nunca se muestra (write-only). Ver
/// docs/superpowers/specs/2026-10-22-pasarela-documentos-electronicos-design.md §7.
/// </summary>
public sealed class CompanyDteSetting
{
    public long Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Código del adaptador registrado (IDteProvider.Code, ej. "FACELE").</summary>
    public string ProviderCode { get; set; } = null!;

    /// <summary>"certification" | "production" -- ver DteEnvironments.</summary>
    public string Environment { get; set; } = DteEnvironments.Certification;

    public string BaseUrl { get; set; } = null!;
    public string Username { get; set; } = null!;

    /// <summary>Cifrado con ISecretoCifradoService. Write-only. null = sin contraseña cargada.</summary>
    public string? SecretKey { get; set; }

    public bool IsEnabled { get; set; }

    // Emisor (Encabezado/Emisor del DTE).
    public string IssuerTaxId { get; set; } = null!;
    public string IssuerName { get; set; } = null!;
    public string IssuerActivity { get; set; } = null!;
    public int IssuerActivityCode { get; set; }
    public string IssuerAddress { get; set; } = null!;
    public string IssuerCommune { get; set; } = null!;
    public string IssuerCity { get; set; } = null!;
    public long? IssuerSiiBranchCode { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class DteEnvironments
{
    public const string Certification = "certification";
    public const string Production = "production";

    public static readonly IReadOnlyList<string> All = new[] { Certification, Production };
}
