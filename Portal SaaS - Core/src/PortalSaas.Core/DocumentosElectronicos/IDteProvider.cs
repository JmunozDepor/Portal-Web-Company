using PortalSaas.Abstractions.Modelos.Dte;

namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>Credenciales y emisor con que se llama al proveedor (ya descifradas; nunca se loguean).</summary>
public sealed record DteProviderContext(string BaseUrl, string Username, string Password, string IssuerTaxId, string Environment);

/// <summary>Datos del emisor tomados de company_dte_settings.</summary>
public sealed record DteIssuerData(
    string TaxId, string Name, string Activity, int ActivityCode, string Address, string Commune, string City, long? SiiBranchCode);

/// <summary>
/// Resultado de reservar folio. UuidAlreadyUsed = el proveedor ya tenía un documento con esa clave
/// (reintento tras una caída): Folio trae el folio original y se continúa con la confirmación.
/// </summary>
public sealed record DteReserveResult(bool Ok, long? Folio, string? Error, bool UuidAlreadyUsed);

public sealed record DteConfirmResult(bool Ok, string? SignedXml, string? Error);

/// <summary>Lo que sabe hacer el proveedor; la plataforma se comporta según esto, nunca según su nombre.</summary>
public sealed record DteProviderCapabilities(IReadOnlyList<int> SupportedTypes, bool TwoPhase, bool SupportsPreview, bool SupportsRelease);

/// <summary>
/// Adaptador de un proveedor de documentos electrónicos (FACELE, BES, ...). Los rechazos de negocio
/// vuelven en Error; las fallas de red/servicio lanzan <see cref="DteProviderUnavailableException"/>.
/// </summary>
public interface IDteProvider
{
    string Code { get; }
    DteProviderCapabilities Capabilities { get; }

    string BuildXml(DteDocument doc, DteIssuerData issuer);
    Task<byte[]> PreviewAsync(DteProviderContext ctx, string xml, int dteType, CancellationToken ct);
    Task<DteReserveResult> ReserveAsync(DteProviderContext ctx, string xml, int dteType, Guid emissionKey, CancellationToken ct);
    Task<DteConfirmResult> ConfirmAsync(DteProviderContext ctx, int dteType, long folio, CancellationToken ct);
    Task ReleaseAsync(DteProviderContext ctx, int dteType, long folio, CancellationToken ct);
    Task<byte[]> GetPdfAsync(DteProviderContext ctx, int dteType, long folio, bool cedible, int copies, CancellationToken ct);

    /// <summary>Prueba de conexión de solo lectura (no emite ni consume folio).</summary>
    Task TestAsync(DteProviderContext ctx, CancellationToken ct);
}

/// <summary>
/// El proveedor no respondió o respondió fuera del protocolo (HTTP != 200, timeout, SOAP inválido).
/// El mensaje NUNCA incluye credenciales ni headers: solo operación y código.
/// </summary>
public sealed class DteProviderUnavailableException : Exception
{
    public DteProviderUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
