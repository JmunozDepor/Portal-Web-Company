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

    // Recepción de documentos (facturas de proveedor). Usa las mismas credenciales que la emisión, pero el
    // servicio vive en otra URL base (FACELE: .../cl-dol, la emisión en .../cl-dol-auth).
    public bool ReceptionEnabled { get; set; }
    public string? ReceptionBaseUrl { get; set; }
    /// <summary>Correo del usuario del proveedor con que se opera la bandeja (FACELE: userMail).</summary>
    public string? ReceptionUserMail { get; set; }
    public int ReceptionIntervalMinutes { get; set; } = 30;
    /// <summary>Días hacia atrás de la primera sincronización (historial para el saldo de OC).</summary>
    public int ReceptionInitialDays { get; set; } = 60;
    /// <summary>Días hacia atrás de cada sincronización posterior (ventana móvil).</summary>
    public int ReceptionWindowDays { get; set; } = 15;
    public DateTimeOffset? ReceptionInitialLoadDoneAt { get; set; }
    public DateTimeOffset? ReceptionLastSyncAt { get; set; }
    public string? ReceptionLastError { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class DteEnvironments
{
    public const string Certification = "certification";
    public const string Production = "production";

    public static readonly IReadOnlyList<string> All = new[] { Certification, Production };
}
