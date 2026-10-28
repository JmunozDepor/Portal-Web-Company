namespace PortalSaas.Abstractions.Modelos.Dte;

/// <summary>
/// Respuestas a un documento recibido (Registro de Reclamos del SII, Ley 19.983 modificada por la Ley 20.956),
/// en términos neutros: cada adaptador las traduce a su proveedor (FACELE: Acepta_Contenido_Documento, ...).
/// </summary>
public static class DteResponseActions
{
    /// <summary>ACD: acepta el contenido del documento.</summary>
    public const string AcceptContent = "accept_content";
    /// <summary>RCD: reclamo al contenido del documento.</summary>
    public const string ClaimContent = "claim_content";
    /// <summary>RFP: reclamo por falta parcial de mercaderías.</summary>
    public const string ClaimPartialGoods = "claim_partial_goods";
    /// <summary>RFT: reclamo por falta total de mercaderías.</summary>
    public const string ClaimTotalGoods = "claim_total_goods";
    /// <summary>ERM: otorga recibo de mercaderías.</summary>
    public const string GoodsReceipt = "goods_receipt";

    public static readonly IReadOnlyList<string> All = new[] { AcceptContent, ClaimContent, ClaimPartialGoods, ClaimTotalGoods, GoodsReceipt };

    public static bool IsClaim(string? action) =>
        action is ClaimContent or ClaimPartialGoods or ClaimTotalGoods;
}

/// <summary>Un documento referenciado por un DTE recibido (ej. Type "801" = orden de compra, Folio = DocNum en SAP).</summary>
public sealed record DteReceivedReference(string Type, string Folio, DateOnly? Date, int? Code, string? Reason);

/// <summary>Estado de la respuesta de un documento recibido en la bandeja de plataforma.</summary>
public static class DteResponseStatuses
{
    public const string None = "none";
    /// <summary>Enviada al proveedor sin confirmación todavía (o el proveedor dijo "ya respondida").</summary>
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
    /// <summary>Respondida por otro canal (ej. el portal del proveedor).</summary>
    public const string External = "external";
}

/// <summary>Origen de una OC asociada a un recibido.</summary>
public static class DtePurchaseOrderRefSources
{
    /// <summary>Referencia 801 informada en el propio DTE.</summary>
    public const string Dte = "dte";
    /// <summary>Asociada a mano en el portal.</summary>
    public const string Manual = "manual";
}

/// <summary>Una OC (DocNum de SAP) asociada a un documento recibido.</summary>
public sealed record DtePurchaseOrderRef(int DocNum, string Source, Guid? UserId, DateTimeOffset At);

/// <summary>Una línea de detalle de un documento recibido, leída del XML archivado.</summary>
public sealed record DteReceivedLine(int LineNumber, string? ItemCode, string Name, decimal Quantity, decimal? UnitPrice, long Amount,
    string? Description = null, string? Unit = null, decimal? DiscountPercent = null, long? DiscountAmount = null);

/// <summary>Emisor o receptor de un DTE, leído del XML. PostalAddress = dirección postal con comuna y ciudad.</summary>
public sealed record DteParty(
    string TaxId, string Name, string? Activity, string? Address, string? Commune, string? City, string? Phone = null,
    string? Email = null, string? Contact = null, string? PostalAddress = null);

/// <summary>Datos de transporte de un DTE recibido (distinto del DteTransport de emisión).</summary>
public sealed record DteReceivedTransport(
    string? Plate, string? CarrierTaxId, string? DriverTaxId, string? DriverName, string? Address, string? Commune, string? City);

/// <summary>Otro impuesto o retención del DTE (ImptoReten).</summary>
public sealed record DteOtherTax(int Code, decimal? Rate, long Amount);

/// <summary>
/// Documento recibido completo, leído del XML (para mostrarlo como el operador). PaymentForm es el código del SII
/// (1 contado, 2 crédito, 3 sin costo); los indicadores van en el código del SII tal cual.
/// </summary>
public sealed record DteReceivedDocument(
    int DteType, long Folio, DateOnly? IssueDate, DateOnly? DueDate, int? PaymentForm, string? PaymentTerms, int? DispatchType,
    int? TransferIndicator, int? ServiceIndicator, bool GrossAmounts, DteParty Issuer, DteParty Receiver, DteReceivedTransport? Transport,
    IReadOnlyList<DteReceivedLine> Lines, IReadOnlyList<DteReceivedReference> References,
    long Net, long Exempt, decimal? VatRate, long Vat, IReadOnlyList<DteOtherTax> OtherTaxes, long Total);

/// <summary>Documento recibido tal como lo ven los módulos (sin el XML).</summary>
public sealed record DteReceivedDto
{
    public long Id { get; init; }
    public string ProviderCode { get; init; } = "";
    public string IssuerTaxId { get; init; } = "";
    public string IssuerName { get; init; } = "";
    public int DteType { get; init; }
    public long Folio { get; init; }
    public DateOnly IssueDate { get; init; }
    public DateTimeOffset? SiiReceivedAt { get; init; }
    public long NetAmount { get; init; }
    public long ExemptAmount { get; init; }
    public long VatAmount { get; init; }
    public long TotalAmount { get; init; }
    public string? SiiStatus { get; init; }
    public string? CommercialStatus { get; init; }
    public string? ClaimStatus { get; init; }
    public bool IsAssigned { get; init; }
    public bool IsVoided { get; init; }
    /// <summary>Por qué el SII no admite aceptar ni reclamar el documento (ej. pagado al contado); null si se puede responder.</summary>
    public string? NotRespondableReason { get; init; }
    public bool HasXml { get; init; }
    /// <summary>El operador informó un enlace al documento: se puede pedir el PDF (IDteInboxService.GetPdfAsync).</summary>
    public bool HasPdf { get; init; }
    /// <summary>Forma de pago en el texto del operador (FACELE: CONTADO, CREDITO, SIN_COSTO); null si no la informa.</summary>
    public string? PaymentForm { get; init; }
    public DateOnly? DueDate { get; init; }
    public IReadOnlyList<DteReceivedReference> References { get; init; } = Array.Empty<DteReceivedReference>();
    public IReadOnlyList<DtePurchaseOrderRef> PurchaseOrderRefs { get; init; } = Array.Empty<DtePurchaseOrderRef>();
    public string ResponseStatus { get; init; } = DteResponseStatuses.None;
    public string? ResponseAction { get; init; }
    public string? ResponseReason { get; init; }
    public string? ResponseActor { get; init; }
    public DateTimeOffset? RespondedAt { get; init; }
    public string? LastError { get; init; }
    public string? ProcessingModule { get; init; }
    public string? ProcessingState { get; init; }
    public string? ProcessingRef { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Filtro de la bandeja. Las fechas son de recepción en el SII (fecha de Chile).</summary>
public sealed record DteReceivedFilter(DateOnly? ReceivedFrom = null, DateOnly? ReceivedTo = null, string? Text = null, int Limit = 1000);

/// <summary>Estado de la recepción de la compañía de la sesión y lo que su proveedor permite hacer.</summary>
public sealed record DteInboxStatus(
    bool ReceptionEnabled, string? ProviderCode, IReadOnlyList<int> RespondableTypes, IReadOnlyList<string> SupportedActions,
    bool SupportsResponseNote, bool SupportsDocumentDownload, int InitialHistoryDays, int IntervalMinutes,
    DateTimeOffset? LastSyncAt, string? LastSyncError);

/// <summary>
/// Pedido de respuesta. Actor = "user:&lt;guid&gt;" o "rule:&lt;código&gt;". ResponseKey la genera el módulo y es estable:
/// repetir el pedido con la misma clave nunca responde dos veces.
/// </summary>
public sealed record DteRespondRequest(long ReceivedId, string Action, string? Reason, string Actor, string ResponseKey);

public enum DteRespondOutcome
{
    Sent,
    /// <summary>El operador dice que ya estaba respondida; lo confirma la próxima sincronización.</summary>
    PendingConfirmation,
    AlreadyResponded,
    Failed,
    NotRespondable,
}

public sealed record DteRespondResult(DteRespondOutcome Outcome, string? Error);
