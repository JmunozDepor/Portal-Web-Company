namespace PortalSaas.Abstractions.Modelos.Dte;

/// <summary>Estados de una emisión en dte_emissions.status.</summary>
public static class DteEmissionStatuses
{
    public const string Pending = "pending";
    public const string FolioReserved = "folio_reserved";
    public const string Issued = "issued";
    public const string Failed = "failed";
    public const string Released = "released";
    public const string Abandoned = "abandoned";

    public static bool IsFinal(string status) => status is Issued or Released or Abandoned;
}

/// <summary>
/// Solicitud de emisión. <see cref="EmissionKey"/> la genera el módulo y es estable: repetirla nunca
/// emite dos documentos. SourceRef identifica el origen de negocio (ej. "manifest:12/guide:1").
/// </summary>
public sealed record DteEmissionRequest(Guid EmissionKey, string SourceModule, string SourceRef, DteDocument Document);

public sealed record DteEmissionResult(Guid EmissionKey, string Status, long? Folio, string? Error, string ProviderCode, string Environment);

/// <summary>Error de validación; Field es el nombre del tag SII (ej. "RUTTrans", "Detalle").</summary>
public sealed record DteValidationError(string Field, string Message);

public sealed record DtePreviewResult(IReadOnlyList<DteValidationError> Errors, byte[]? Pdf);

public sealed record DteConfigurationStatus(
    bool IsConfigured, string? ProviderCode, string? Environment, string? IssuerTaxId,
    IReadOnlyList<int> SupportedTypes, string? Error, string? IssuerName = null, string? IssuerActivity = null);
