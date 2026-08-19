namespace Servicios.TransferenciaAutomatica_v2.Domain;

/// <summary>
/// Mapeo ObjType SAP -> (tipo de documento para Service Layer, tabla detalle, tabla
/// cabecera, columna de bodega destino). Mismo mapeo que v1 (Servicios.TransferenciaAutomatica/
/// Sap/DocumentTypeMapping.cs) -- son códigos de objeto estándar de SAP Business One
/// (17=Orden de venta, 13=Factura de venta, 1250000001=Solicitud de traslado de
/// inventario), no datos de configuración por compañía, así que se mantienen como
/// literales acá también.
///
/// ColumnaBodegaDestino difiere del legado (SP_DEP_ORDER_ABS): RDR1/INV1 usan "WhsCode",
/// pero WTQ1 usa "FromWhsCod" como la bodega cuya necesidad hay que cubrir -- así estaba
/// en el SP original, se preserva la misma distinción acá.
/// </summary>
public sealed record TipoDocumento(string ServiceLayerRefObjType, string TablaDetalle, string TablaCabecera, string ColumnaBodegaDestino);

public static class DocumentTypeMapping
{
    public static bool TryResolver(string objType, out TipoDocumento tipoDocumento)
    {
        tipoDocumento = objType switch
        {
            "17" => new TipoDocumento("rot_SalesOrder", "RDR1", "ORDR", "WhsCode"),
            "13" => new TipoDocumento("rot_SalesInvoice", "INV1", "OINV", "WhsCode"),
            "1250000001" => new TipoDocumento("rot_InventoryTransferRequest", "WTQ1", "OWTQ", "FromWhsCod"),
            _ => null!
        };

        return tipoDocumento is not null;
    }
}
