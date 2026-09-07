using System.Text.Json.Serialization;
using PortalSaas.Core.Sap;

namespace PortalSaas.Core.Inventario;

/// <summary>
/// Forma exacta que espera/devuelve el recurso de Service Layer de cada tipo de
/// documento de inventario (ver InventoryDocumentTypeCatalog) -- nunca expuesto fuera
/// de Core. Nombres de propiedad en PascalCase tal cual los define SAP.
///
/// Dos particularidades confirmadas contra referencia-original/PortalSAP_v2 (CLAUDE.md,
/// errores reales del ambiente, no supuestos):
/// - El arreglo de líneas va bajo "StockTransferLines", NO "DocumentLines" (a diferencia
///   de Orders/PurchaseOrders/Invoices) -- con "DocumentLines" el JSON nunca matchea y
///   la tab Contenido queda vacía.
/// - Sin DocDueDate -- Service Layer la rechaza para StockTransfer ("Property
///   'DocDueDate' of 'StockTransfer' is invalid"). Solo DocDate.
/// Sin FromWarehouse/ToWarehouse de cabecera -- el almacén es por línea (ver
/// SapInventoryDocumentLine), mismo criterio ya confirmado en el original.
/// </summary>
internal sealed class SapInventoryDocumentHeader
{
    public int? DocEntry { get; set; }
    public int? DocNum { get; set; }
    public DateTime? DocDate { get; set; }

    /// <summary>NNM1.Series -- null omite el campo del JSON, SAP asigna la serie por defecto del tipo de documento.</summary>
    public int? Series { get; set; }

    public string? Comments { get; set; }
    public string? DocumentStatus { get; set; }
    public List<SapInventoryDocumentLine> StockTransferLines { get; set; } = [];
    public string? U_PortalUser { get; set; }

    /// <summary>
    /// Socio de negocio de cabecera -- SÍ existe la columna en OWTQ/OWTR (confirmado,
    /// ver InventoryDocumentService.ListAsync que ya la consulta por HANA para el
    /// listado/filtro) y Service Layer la acepta al crear (a diferencia de DocDueDate,
    /// que si la rechaza -- ver el comentario de arriba). Antes no se exponía como
    /// editable en el formulario a propósito; ahora sí, a pedido explícito del dueño
    /// del proyecto (traslados/solicitudes de traslado también manejan socio de
    /// negocio, igual que Venta/Compra).
    /// </summary>
    public string? CardCode { get; set; }
    public string? CardName { get; set; }
    public string? NumAtCard { get; set; }

    /// <summary>Campos de usuario (UDF dinámicos) -- ver SapAdditionalFieldsHelper.</summary>
    public IReadOnlyDictionary<string, object?>? AdditionalFields { get; set; }
}

internal sealed class SapInventoryDocumentLine
{
    public int? LineNum { get; set; }
    public string ItemCode { get; set; } = null!;
    public decimal Quantity { get; set; }

    /// <summary>Almacén destino de esta línea -- nullable, Copy-From no lo necesita (SAP lo deriva del documento base).</summary>
    public string? WarehouseCode { get; set; }

    /// <summary>Almacén origen de esta línea -- nullable, mismo motivo que WarehouseCode.</summary>
    public string? FromWarehouseCode { get; set; }

    /// <summary>
    /// Copy-From -- ver InventoryDocumentLineDto.BaseType/BaseEntry/BaseLine. Service Layer
    /// devuelve estos campos como número o como string segun el contexto (BaseType de un
    /// Traslado creado por Copy-From vuelve como string) -- ver FlexibleNullableInt32Converter.
    /// </summary>
    [JsonConverter(typeof(FlexibleNullableInt32Converter))]
    public int? BaseType { get; set; }

    [JsonConverter(typeof(FlexibleNullableInt32Converter))]
    public int? BaseEntry { get; set; }

    [JsonConverter(typeof(FlexibleNullableInt32Converter))]
    public int? BaseLine { get; set; }

    /// <summary>Campos de usuario de línea -- ver SapInventoryDocumentHeader.AdditionalFields.</summary>
    public IReadOnlyDictionary<string, object?>? AdditionalFields { get; set; }
}
