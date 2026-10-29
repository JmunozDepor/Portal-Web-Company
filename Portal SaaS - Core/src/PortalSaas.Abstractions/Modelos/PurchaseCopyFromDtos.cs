namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Pedido de compra abierto con líneas pendientes -- lo lista "Copiar de Pedido" (ver
/// IPurchaseDocumentService.ListOpenOrdersAsync). Non-positional a propósito: HanaService
/// lo instancia vía reflection (mismo criterio que PurchaseDocumentSummaryDto).
/// </summary>
public sealed record PurchaseOpenOrderDto
{
    public int DocEntry { get; init; }
    public int DocNum { get; init; }
    public DateTime DocDate { get; init; }
    public string? SupplierReferenceNumber { get; init; }
    public string? DocCurrency { get; init; }

    /// <summary>En la moneda del Pedido (DocTotalFC si es extranjera).</summary>
    public decimal DocTotal { get; init; }

    /// <summary>OPOR.DocType: "I" Artículo, "S" Servicio.</summary>
    public string DocType { get; init; } = "I";

    public int OpenLineCount { get; init; }

    public DocumentLineType LineType => DocType == "S" ? DocumentLineType.Service : DocumentLineType.Item;
}

/// <summary>
/// Línea pendiente de un Pedido abierto (POR1.OpenQty &gt; 0) -- lo que "Copiar de Pedido"
/// trae a la factura y contra lo que se valida al crearla. UnitPrice es el precio ANTES
/// del descuento (POR1.PriceBefDi), igual que UnitPrice de Service Layer.
/// </summary>
public sealed record PurchaseOpenOrderLineDto
{
    public int DocEntry { get; init; }
    public int DocNum { get; init; }
    public int LineNum { get; init; }
    public string CardCode { get; init; } = string.Empty;
    public string DocType { get; init; } = "I";
    public string? DocCurrency { get; init; }

    /// <summary>OPOR.SlpCode -- Comprador del Pedido (-1 = sin comprador).</summary>
    public int? BuyerCode { get; init; }

    public string? ItemCode { get; init; }
    public string? Description { get; init; }
    public decimal OpenQuantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public string? Currency { get; init; }
    public decimal DiscountPercent { get; init; }
    public string? WarehouseCode { get; init; }
    public string? AccountCode { get; init; }
    public string? CostCenterCode { get; init; }
    public string? CostCenterCode2 { get; init; }
    public string? CostCenterCode3 { get; init; }

    public DocumentLineType LineType => DocType == "S" ? DocumentLineType.Service : DocumentLineType.Item;
}
