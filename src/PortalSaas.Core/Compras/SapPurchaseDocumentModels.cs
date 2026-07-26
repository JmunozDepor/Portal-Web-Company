namespace PortalSaas.Core.Compras;

/// <summary>
/// Forma exacta que espera/devuelve el recurso de Service Layer de cada tipo de
/// documento de compra (ver PurchaseDocumentTypeCatalog) -- nunca expuesto fuera de
/// Core. Nombres de propiedad en PascalCase tal cual los define SAP -- mismo shape que
/// SapSalesDocumentHeader/Line (los documentos de compra y venta comparten la
/// estructura base de "documento de marketing" de SAP B1), "CardCode"/"CardName" acá
/// identifican al proveedor, no al cliente.
/// </summary>
internal sealed class SapPurchaseDocumentHeader
{
    public int? DocEntry { get; set; }
    public int? DocNum { get; set; }
    public string CardCode { get; set; } = null!;
    public string? CardName { get; set; }
    public string? Comments { get; set; }
    public DateTime? DocDate { get; set; }
    public DateTime? DocDueDate { get; set; }
    public string? NumAtCard { get; set; }
    public decimal? DocTotal { get; set; }
    public string? DocumentStatus { get; set; }
    public List<SapPurchaseDocumentLine> DocumentLines { get; set; } = [];
    public string? U_PortalUser { get; set; }
}

internal sealed class SapPurchaseDocumentLine
{
    public int? LineNum { get; set; }
    public string ItemCode { get; set; } = null!;
    public string? ItemDescription { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Null = omitido del JSON -- SAP asigna el precio de la lista propia del proveedor.</summary>
    public decimal? UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }
    public string WarehouseCode { get; set; } = null!;
}
