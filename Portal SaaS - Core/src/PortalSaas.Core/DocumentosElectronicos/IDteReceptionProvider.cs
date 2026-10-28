using PortalSaas.Abstractions.Modelos.Dte;

namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>
/// Credenciales y receptor con que se consulta la bandeja del proveedor (ya descifradas; nunca se loguean).
/// ReceiverTaxId = el RUT de la compañía (el "abonado"); UserMail = usuario del proveedor con que se opera.
/// DocumentsBaseUrl = servidor de donde se bajan el XML/PDF de los recibidos (FACELE: DocumentosRecibidosService, que vive
/// en el servidor de emisión; en Depor el DoceleOL local); null = el de recepción.
/// </summary>
public sealed record DteReceptionContext(string BaseUrl, string Username, string Password, string ReceiverTaxId, string UserMail, string Environment,
    string? DocumentsBaseUrl = null);

/// <summary>Lo que sabe hacer el proveedor en recepción; la plataforma y los módulos se comportan según esto.</summary>
public sealed record DteReceptionCapabilities(
    IReadOnlyList<int> RespondableTypes, IReadOnlyList<string> SupportedActions, bool SupportsResponseNote, bool SupportsDocumentDownload);

/// <summary>
/// Un documento recibido tal como lo informa el proveedor. Los estados van en el texto crudo del proveedor;
/// RespondedAction es la respuesta ya registrada en el operador traducida a DteResponseActions (null si no hay), para
/// que la plataforma no conozca los textos de cada proveedor. NotRespondableReason (null si se puede responder) dice,
/// en español, por qué el SII no admite aceptar ni reclamar el documento (ej. factura pagada al contado).
/// Url = enlace al documento (PDF) si el proveedor lo informa; PaymentForm = forma de pago en el texto del proveedor
/// (FACELE: CONTADO, CREDITO, SIN_COSTO); DueDate = fecha de vencimiento del documento.
/// </summary>
public sealed record DteReceivedRecord(
    string IssuerTaxId, string IssuerName, int DteType, long Folio, DateOnly IssueDate, DateTimeOffset? SiiReceivedAt,
    long Net, long Exempt, long Vat, long Total, string? OtherTaxesJson, string? SiiStatus, string? CommercialStatus,
    string? ClaimStatus, bool IsAssigned, bool IsVoided, string? Url, string? RespondedAction = null,
    string? NotRespondableReason = null, string? PaymentForm = null, DateOnly? DueDate = null);

/// <summary>
/// Resultado de responder un recibido. AlreadyResponded = el proveedor dice que el documento ya tenía respuesta
/// (de este portal en un intento anterior o de otro canal): lo confirma la próxima sincronización.
/// </summary>
public sealed record DteRespondProviderResult(bool Ok, bool AlreadyResponded, string? Error);

/// <summary>
/// Adaptador de recepción de un proveedor de documentos electrónicos (FACELE, BES, Acepta, ...). Los rechazos de
/// negocio vuelven en el resultado; las fallas de red/servicio lanzan <see cref="DteProviderUnavailableException"/>.
/// </summary>
public interface IDteReceptionProvider
{
    string Code { get; }
    DteReceptionCapabilities Capabilities { get; }

    /// <summary>Recibidos con fecha de emisión entre from y to (inclusive). Pagina internamente.</summary>
    Task<IReadOnlyList<DteReceivedRecord>> ListReceivedAsync(DteReceptionContext ctx, DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyList<DteReceivedReference>> GetReferencesAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, CancellationToken ct);

    /// <summary>XML del documento; null si el proveedor no lo entrega (SupportsDocumentDownload = false).</summary>
    Task<string?> GetXmlAsync(DteReceptionContext ctx, DteReceivedRecord record, CancellationToken ct);

    /// <summary>PDF del documento; null si el proveedor no lo entrega.</summary>
    Task<byte[]?> GetPdfAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, string? url, CancellationToken ct);

    /// <summary>Respuesta ante el SII (action = una de DteResponseActions).</summary>
    Task<DteRespondProviderResult> RespondAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, string action, CancellationToken ct);

    /// <summary>Deja el motivo escrito en el proveedor (solo si SupportsResponseNote).</summary>
    Task SetNoteAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, string note, CancellationToken ct);

    /// <summary>Prueba de conexión de solo lectura.</summary>
    Task TestAsync(DteReceptionContext ctx, CancellationToken ct);
}
