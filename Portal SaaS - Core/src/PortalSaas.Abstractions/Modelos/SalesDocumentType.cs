namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Los 7 tipos de documento de venta que soporta el motor genérico (ver
/// ISalesDocumentService/SalesDocumentTypeCatalog) -- portado de
/// TipoDocumentoGenericoVenta en referencia-original/PortalSAP_v2. Factura Deudores,
/// Factura Reserva y Recibo comparten tabla/recurso SAP ("OINV"/"Invoices"), se
/// distinguen por los filtros extra del catálogo (isIns/DocSubType), no por el enum.
/// </summary>
public enum SalesDocumentType
{
    SalesOrder,
    CreditNote,
    CustomerInvoice,
    ReserveInvoice,
    ReturnRequest,
    Return,
    Receipt,
}
