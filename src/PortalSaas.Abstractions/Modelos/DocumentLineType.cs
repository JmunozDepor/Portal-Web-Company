namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Tipo de línea de un documento de Venta o Compra -- Artículo o Servicio, portado de
/// TipoLineaGenericoVenta/TipoLineaGenericoCompra en referencia-original/PortalSAP_v2.
/// No aplica a Inventario -- OWTQ/OWTR (traslado entre almacenes) no tienen concepto de
/// línea de Servicio en SAP, solo mueven artículos físicos.
///
/// SAP no permite mezclar líneas de Artículo y Servicio en el mismo documento -- el
/// campo real de Service Layer (DocType: "dDocument_Items"/"dDocument_Service") es de
/// CABECERA, no por línea (ver SalesDocumentService/PurchaseDocumentService,
/// ResolveDocType). Por eso acá el tipo se elige una sola vez por documento (un único
/// <select> en la tab General) y se aplica igual a todas las líneas al construir el
/// DTO -- a diferencia del original (que sincroniza un campo oculto por línea vía JS),
/// esto lo hace el servidor en OnPostAsync, sin necesitar JS nuevo.
/// </summary>
public enum DocumentLineType
{
    Item,
    Service,
}
