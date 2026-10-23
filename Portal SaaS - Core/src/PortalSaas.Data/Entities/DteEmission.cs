namespace PortalSaas.Data.Entities;

/// <summary>
/// Registro central de emisiones DTE (todos los módulos): idempotencia por (CompanyId,
/// EmissionKey), folio, estado del ciclo con el proveedor y XML enviado / timbrado (archivo
/// legal). El módulo de origen guarda solo su EmissionKey. Historial real: no se borra.
/// Estados en DteEmissionStatuses (Abstractions).
/// </summary>
public sealed class DteEmission
{
    public long Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid EmissionKey { get; set; }
    public string SourceModule { get; set; } = null!;
    public string SourceRef { get; set; } = null!;
    public int DteType { get; set; }

    // Con qué se emitió (trazabilidad si la configuración cambia después).
    public string ProviderCode { get; set; } = null!;
    public string IssuerTaxId { get; set; } = null!;
    public string Environment { get; set; } = null!;

    public long? Folio { get; set; }
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    /// <summary>SHA-256 (hex) de RequestJson: detecta "misma clave, distinto contenido".</summary>
    public string RequestHash { get; set; } = null!;

    /// <summary>DteDocument serializado (System.Text.Json).</summary>
    public string RequestJson { get; set; } = null!;

    public string? XmlSent { get; set; }
    public string? XmlSigned { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReservedAt { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; set; }
}
