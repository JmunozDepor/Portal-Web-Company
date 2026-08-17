namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// MapperKey fijos que usan los consumidores de wms_oracle_field_mappings.
/// Los primeros 6 (WMS -> SAP, confirmaciones) los usa WmsSapIntegration.Service
/// (standalone, ver ARQUITECTURA.md de Modulo.Wms) -- contrato manual de strings
/// entre ese servicio y este módulo, sin proyecto compartido. Portados tal cual desde
/// WMS_Suite/WmsPortal.Core/Models/FieldMappingModels.cs (FieldMappingKeys.Labels).
/// Los 6 SAPWMS_* (SAP -> WMS, Subida) los usa WmsCloudConnector, en este mismo repo --
/// ver docs/superpowers/specs/2026-08-16-mapeo-campos-sap-wms-design.md de Modulo.Wms.
/// Si se agrega un mapeador nuevo de cualquiera de los dos lados, agregar la entrada
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
        ["SAPWMS_ITEM"] = "SAP → WMS: Item",
        ["SAPWMS_STORE"] = "SAP → WMS: Sucursal",
        ["SAPWMS_ORDER_HDR"] = "SAP → WMS: Orden (cabecera)",
        ["SAPWMS_ORDER_DTL"] = "SAP → WMS: Orden (detalle)",
        ["SAPWMS_INBOUND_HDR"] = "SAP → WMS: Ingreso (cabecera)",
        ["SAPWMS_INBOUND_DTL"] = "SAP → WMS: Ingreso (detalle)",
    };
}
