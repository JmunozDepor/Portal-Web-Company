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
