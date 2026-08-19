namespace Servicios.TransferenciaAutomatica.Sap;

/// <summary>
/// Mapeo ObjType SAP -> (tipo de documento para Service Layer, tabla detalle, tabla
/// cabecera). Copiado tal cual del switch de StrockTrans.StockTransferSearch del servicio
/// legado -- son códigos de objeto estándar de SAP Business One (17=Orden de venta,
/// 13=Factura de venta, 1250000001=Solicitud de traslado de inventario), no datos de
/// configuración por compañía, así que se mantienen como literales acá igual que en el
/// legado (ver CLAUDE.md: "no reescribir el algoritmo").
/// </summary>
public sealed record TipoDocumento(string ServiceLayerRefObjType, string TablaDetalle, string TablaCabecera);

public static class DocumentTypeMapping
{
    public static bool TryResolver(string objType, out TipoDocumento tipoDocumento)
    {
        tipoDocumento = objType switch
        {
            "17" => new TipoDocumento("rot_SalesOrder", "RDR1", "ORDR"),
            "13" => new TipoDocumento("rot_SalesInvoice", "INV1", "OINV"),
            "1250000001" => new TipoDocumento("rot_InventoryTransferRequest", "WTQ1", "OWTQ"),
            _ => null!
        };

        return tipoDocumento is not null;
    }
}
