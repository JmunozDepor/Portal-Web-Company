namespace PortalSaas.Core.Ventas;

/// <summary>
/// Forma exacta que espera/devuelve el recurso de Service Layer de cada tipo de
/// documento de venta (ver SalesDocumentTypeCatalog) -- nunca expuesto fuera de Core
/// (SalesDocumentService lo mapea desde/hacia SalesDocumentDto, ver
/// PortalSaas.Abstractions.Modelos.SalesDocumentDto). Nombres de propiedad en
/// PascalCase tal cual los define SAP -- no son "vocabulario de negocio" a traducir,
/// son el contrato de red del proveedor. Generaliza el antiguo SapSalesOrderHeader/Line
/// (mismo shape, ahora compartido por los 7 tipos).
/// </summary>
internal sealed class SapSalesDocumentHeader
{
    public int? DocEntry { get; set; }
    public int? DocNum { get; set; }

    /// <summary>
    /// "dDocument_Items" | "dDocument_Service" -- de CABECERA, no confundir con el
    /// ItemType de cada línea. Sin esto SAP asume dDocument_Items por defecto y exige
    /// ItemCode aunque la línea venga marcada como itService (error real documentado
    /// en la referencia: "Item number is missing"). Ver SalesDocumentService.ResolveDocType.
    /// </summary>
    public string DocType { get; set; } = "dDocument_Items";

    public string CardCode { get; set; } = null!;
    public string? CardName { get; set; }
    public int? SalesPersonCode { get; set; }

    /// <summary>NNM1.Series -- null omite el campo del JSON, SAP asigna la serie por defecto del tipo de documento.</summary>
    public int? Series { get; set; }

    /// <summary>OSHP.TrnspCode -- tab Logística, solo Venta.</summary>
    public int? TransportationCode { get; set; }

    /// <summary>OCTG.GroupNum -- tab Finanzas, solo Venta.</summary>
    public int? GroupNumber { get; set; }
    public string? Comments { get; set; }
    public DateTime? DocDate { get; set; }
    public DateTime? DocDueDate { get; set; }
    public DateTime? TaxDate { get; set; }
    public string? NumAtCard { get; set; }
    public decimal? DocTotal { get; set; }
    public string? DocumentStatus { get; set; }
    public List<SapSalesDocumentLine> DocumentLines { get; set; } = [];
    public string? U_PortalUser { get; set; }

    /// <summary>Campos de usuario (UDF dinámicos) -- ver SapAdditionalFieldsHelper. Nunca se serializa tal cual, se aplana antes de postear.</summary>
    public IReadOnlyDictionary<string, object?>? AdditionalFields { get; set; }
}

internal sealed class SapSalesDocumentLine
{
    public int? LineNum { get; set; }

    /// <summary>"itItems" | "itService" -- confirmado contra la referencia, no "LineType"/"cItem"/"cService".</summary>
    public string? ItemType { get; set; }

    public string? ItemCode { get; set; }
    public string? ItemDescription { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Null = omitido del JSON -- SAP asigna el precio de la lista propia del cliente.</summary>
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

    /// <summary>Copy-From -- ver SalesDocumentLineDto.BaseType/BaseEntry/BaseLine.</summary>
    public int? BaseType { get; set; }
    public int? BaseEntry { get; set; }
    public int? BaseLine { get; set; }

    /// <summary>Campos de usuario de línea -- ver SapSalesDocumentHeader.AdditionalFields.</summary>
    public IReadOnlyDictionary<string, object?>? AdditionalFields { get; set; }
}
