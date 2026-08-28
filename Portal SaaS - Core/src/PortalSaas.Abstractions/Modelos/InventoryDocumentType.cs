namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Los 2 tipos de documento de inventario que soporta el motor genérico (ver
/// IInventoryDocumentService/InventoryDocumentTypeCatalog) -- portado de
/// TipoDocumentoGenericoInventario en referencia-original/PortalSAP_v2. Dominio más
/// simple que Venta/Compra: sin cliente/proveedor, solo traslado de mercadería entre
/// almacenes.
/// </summary>
public enum InventoryDocumentType
{
    InventoryTransferRequest,
    StockTransfer,
}
