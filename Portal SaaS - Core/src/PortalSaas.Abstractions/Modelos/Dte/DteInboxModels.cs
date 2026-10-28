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
