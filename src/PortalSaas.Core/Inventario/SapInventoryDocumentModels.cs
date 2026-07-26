namespace PortalSaas.Core.Inventario;

/// <summary>
/// Forma exacta que espera/devuelve el recurso de Service Layer de cada tipo de
/// documento de inventario (ver InventoryDocumentTypeCatalog) -- nunca expuesto fuera
/// de Core. Nombres de propiedad en PascalCase tal cual los define SAP. A diferencia de
/// Venta/Compra, SAP exige el par de almacenes POR LÍNEA (WarehouseCode = destino,
/// FromWarehouseCode = origen) aunque el documento entero mueva todo entre el mismo par
/// -- InventoryDocumentService copia los defaults del encabezado a cada línea al armar
/// el POST (ver el doc-comment de InventoryDocumentDto).
/// </summary>
internal sealed class SapInventoryDocumentHeader
{
    public int? DocEntry { get; set; }
    public int? DocNum { get; set; }
    public DateTime? DocDate { get; set; }
    public DateTime? DocDueDate { get; set; }
    public string? Comments { get; set; }
    public string? FromWarehouse { get; set; }
    public string? ToWarehouse { get; set; }
    public string? DocumentStatus { get; set; }
    public List<SapInventoryDocumentLine> DocumentLines { get; set; } = [];
    public string? U_PortalUser { get; set; }
}

internal sealed class SapInventoryDocumentLine
{
    public int? LineNum { get; set; }
    public string ItemCode { get; set; } = null!;
    public decimal Quantity { get; set; }

    /// <summary>Almacén destino de esta línea.</summary>
    public string WarehouseCode { get; set; } = null!;

    /// <summary>Almacén origen de esta línea.</summary>
    public string FromWarehouseCode { get; set; } = null!;
}
