namespace PortalSaas.Core.Ventas;

/// <summary>
/// Forma exacta que espera/devuelve el recurso "Orders" de Service Layer -- nunca
/// expuesto fuera de Core (SalesOrderService lo mapea desde/hacia SalesOrderDto, ver
/// PortalSaas.Abstractions.Modelos.SalesOrderDto). Nombres de propiedad en PascalCase
/// tal cual los define SAP -- no son "vocabulario de negocio" a traducir, son el
/// contrato de red del proveedor.
/// </summary>
internal sealed class SapSalesOrderHeader
{
    public int? DocEntry { get; set; }
    public int? DocNum { get; set; }

    /// <summary>
    /// "dDocument_Items" -- fijo, esta primera entrega solo soporta líneas de
    /// Artículo (ver CLAUDE.md, alcance recortado). Sin esto SAP puede rechazar el
    /// documento con "Item number is missing" en algunos escenarios de líneas mixtas.
    /// </summary>
    public string DocType { get; set; } = "dDocument_Items";

    public string CardCode { get; set; } = null!;
    public string? CardName { get; set; }
    public int? SalesPersonCode { get; set; }
    public string? Comments { get; set; }
    public DateTime? DocDate { get; set; }
    public DateTime? DocDueDate { get; set; }
    public DateTime? TaxDate { get; set; }
    public string? NumAtCard { get; set; }
    public decimal? DocTotal { get; set; }
    public string? DocumentStatus { get; set; }
    public List<SapSalesOrderLine> DocumentLines { get; set; } = [];
    public string? U_PortalUser { get; set; }
}

internal sealed class SapSalesOrderLine
{
    public int? LineNum { get; set; }
    public string ItemCode { get; set; } = null!;
    public string? ItemDescription { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Null = omitido del JSON -- SAP asigna el precio de la lista propia del cliente.</summary>
    public decimal? UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }
    public string WarehouseCode { get; set; } = null!;
}
