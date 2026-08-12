namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Los 6 MapperKey fijos que usa WmsSapIntegration.Service (standalone, ver
/// ARQUITECTURA.md de Modulo.Wms) para resolver el UDF de cada documento SAP
/// Service Layer contra wms_oracle_field_mappings. Es un contrato manual de
/// strings entre ese servicio y este módulo -- no hay proyecto compartido
/// entre las dos soluciones. Portado tal cual desde
/// WMS_Suite/WmsPortal.Core/Models/FieldMappingModels.cs (FieldMappingKeys.Labels).
/// Si se agrega un mapeador nuevo del lado del servicio, agregar la entrada
/// correspondiente acá también.
/// </summary>
public static class WmsFieldMapperKeys
{
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["ORDER_CONFIRM_STOCKTRANSFER"] = "Confirmación Orden → Traslado (StockTransfers)",
        ["ORDER_CONFIRM_DELIVERYNOTE"] = "Confirmación Orden → Entrega (DeliveryNotes)",
        ["ORDER_CONFIRM_XDK_STOCKTRANSFER"] = "Confirmación Orden Crossdocking → Traslado (StockTransfers)",
        ["RECEIPT_CONFIRM_STOCKTRANSFER"] = "Confirmación Ingreso → Traslado (StockTransfers)",
        ["RECEIPT_CONFIRM_RETURN"] = "Confirmación Ingreso → Devolución (Returns)",
        ["RECEIPT_CONFIRM_PURCHASE_DELIVERY"] = "Confirmación Ingreso → Recepción de Compra (PurchaseDeliveryNotes)",
    };
}
