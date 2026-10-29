namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Los tipos de documento de compra que soporta el motor genérico (ver
/// IPurchaseDocumentService/PurchaseDocumentTypeCatalog) -- Oferta/Pedido portados de
/// TipoDocumentoGenericoCompra en referencia-original/PortalSAP_v2; las 2 facturas
/// (OPCH) son nuevas (2026-10-29, el original nunca las tuvo): la de proveedor registra
/// servicios, la de reserva recibe productos. Entrada de mercadería y Notas de débito/
/// crédito de proveedor siguen pendientes.
/// </summary>
public enum PurchaseDocumentType
{
    PurchaseQuotation,
    PurchaseOrder,
    PurchaseInvoice,
    PurchaseReserveInvoice,
}
