namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Los 2 tipos de documento de compra que soporta el motor genérico (ver
/// IPurchaseDocumentService/PurchaseDocumentTypeCatalog) -- portado de
/// TipoDocumentoGenericoCompra en referencia-original/PortalSAP_v2. El resto de la
/// familia de Compras (Entrada de mercadería, Facturas de proveedor, Notas de débito/
/// crédito) queda pendiente, mismo criterio que ya usó el original -- no hace falta
/// para esta entrega.
/// </summary>
public enum PurchaseDocumentType
{
    PurchaseQuotation,
    PurchaseOrder,
}
