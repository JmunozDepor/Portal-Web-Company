namespace PortalSaas.Core.Compras;

/// <summary>
/// Forma exacta que espera/devuelve el recurso de Service Layer de cada tipo de
/// documento de compra (ver PurchaseDocumentTypeCatalog) -- nunca expuesto fuera de
/// Core. Nombres de propiedad en PascalCase tal cual los define SAP -- mismo shape que
/// SapSalesDocumentHeader/Line (los documentos de compra y venta comparten la
/// estructura base de "documento de marketing" de SAP B1), "CardCode"/"CardName" acá
/// identifican al proveedor, no al cliente.
///
/// Particularidad real confirmada contra referencia-original/PortalSAP_v2
/// (GenericoCompraSapModels.cs/GenericoCompraService.cs, error real del ambiente, no
/// supuesto): a diferencia de Venta, Compra exige la fecha requerida tanto en la
/// CABECERA como en cada LÍNEA -- sin esto Service Layer rechaza la creación con
/// "Specify the required date [OINV.ReqDate]" aunque DocDueDate ya venga seteado. El
/// nombre real de la propiedad de cabecera en Service Layer es "RequriedDate" (SAP
/// tiene un typo documentado en el objeto de Compras, letras invertidas respecto a
/// "RequiredDate" en las líneas) -- no es un error de tipeo de este proyecto, es el
/// nombre real que espera la API.
/// </summary>
internal sealed class SapPurchaseDocumentHeader
{
    public int? DocEntry { get; set; }
    public int? DocNum { get; set; }

    /// <summary>"dDocument_Items" | "dDocument_Service" -- ver PurchaseDocumentService.ResolveDocType.</summary>
    public string DocType { get; set; } = "dDocument_Items";

    public string CardCode { get; set; } = null!;
    public string? CardName { get; set; }

    /// <summary>NNM1.Series -- null omite el campo del JSON, SAP asigna la serie por defecto del tipo de documento.</summary>
    public int? Series { get; set; }

    public string? Comments { get; set; }
    public DateTime? DocDate { get; set; }
    public DateTime? DocDueDate { get; set; }
    public DateTime? RequriedDate { get; set; }
    public DateTime? TaxDate { get; set; }
    public string? NumAtCard { get; set; }
    public decimal? DocTotal { get; set; }
    public string? DocumentStatus { get; set; }
    public List<SapPurchaseDocumentLine> DocumentLines { get; set; } = [];
    public string? U_PortalUser { get; set; }

    /// <summary>Campos de usuario (UDF dinámicos) -- ver SapAdditionalFieldsHelper.</summary>
    public IReadOnlyDictionary<string, object?>? AdditionalFields { get; set; }
}

internal sealed class SapPurchaseDocumentLine
{
    public int? LineNum { get; set; }

    /// <summary>"itItems" | "itService" -- confirmado contra la referencia, no "LineType"/"cItem"/"cService".</summary>
    public string? ItemType { get; set; }

    public string? ItemCode { get; set; }
    public string? ItemDescription { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Null = omitido del JSON -- SAP asigna el precio de la lista propia del proveedor.</summary>
    public decimal? UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }

    /// <summary>Solo Artículo.</summary>
    public string? WarehouseCode { get; set; }

    /// <summary>Cuenta Mayor -- solo Servicio.</summary>
    public string? AccountCode { get; set; }

    /// <summary>Centro de Costos (Dimensión1) -- solo Servicio.</summary>
    public string? CostingCode { get; set; }

    /// <summary>Dimensión2 (Marca) -- solo Servicio, opcional.</summary>
    public string? CostingCode2 { get; set; }

    /// <summary>Dimensión3 (Tipo de Gasto) -- solo Servicio, opcional.</summary>
    public string? CostingCode3 { get; set; }

    /// <summary>Fecha requerida por línea -- se postea igual al DocDueDate del encabezado en todas las líneas (el portal no captura una fecha distinta por línea), mismo criterio que el original.</summary>
    public DateTime? RequiredDate { get; set; }

    /// <summary>Campos de usuario de línea -- ver SapPurchaseDocumentHeader.AdditionalFields.</summary>
    public IReadOnlyDictionary<string, object?>? AdditionalFields { get; set; }
}
