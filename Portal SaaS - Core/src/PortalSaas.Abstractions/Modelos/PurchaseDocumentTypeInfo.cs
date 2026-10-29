namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Particularidades de un tipo de documento de compra que el formulario base lee del
/// catálogo (ver IPurchaseDocumentService.Describe) -- regla "todo nace del documento
/// padre": un subtipo nunca las decide, solo se identifica. DefaultLineType es el tipo
/// de línea con que abre un documento nuevo (modificable); HasTaxDocument muestra el
/// documento tributario del proveedor (Indicador/folio/RUT, pestaña Finanzas);
/// CopyFromType habilita "Copiar de" desde ese tipo (hoy solo Pedido).
/// </summary>
public sealed record PurchaseDocumentTypeInfo(DocumentLineType DefaultLineType, bool HasTaxDocument, PurchaseDocumentType? CopyFromType);
