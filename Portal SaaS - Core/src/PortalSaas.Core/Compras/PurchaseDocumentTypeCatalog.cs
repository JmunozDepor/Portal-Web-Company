using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Compras;

/// <summary>
/// Configuración por tipo de documento de compra -- portado de
/// GenericoCompraCatalogo en referencia-original/PortalSAP_v2, mismo patrón de
/// catálogo estático que SalesDocumentTypeCatalog/InventoryDocumentTypeCatalog. Oferta y
/// Pedido se digitan directo, sin el flujo de aprobación que en el original generaba el
/// Pedido a partir de la Oferta -- ver CLAUDE.md.
///
/// Las 2 facturas (2026-10-29) comparten "OPCH"/"PurchaseInvoices", como Factura/Reserva
/// en Venta. A diferencia de SalesDocumentTypeCatalog, el filtro de listado y la marca
/// de creación son datos distintos: ExtraFilters es la columna de la tabla (isIns, solo
/// WHERE) y CreateFields la propiedad de Service Layer que se fija en el POST
/// (ReserveInvoice) -- Service Layer no conoce la columna isIns.
/// </summary>
public static class PurchaseDocumentTypeCatalog
{
    /// <summary>
    /// Ver el doc-comment de SupportsCancel en SalesDocumentTypeCatalog.Entry. Lo demás
    /// lo lee el formulario base vía IPurchaseDocumentService.Describe (ver
    /// PurchaseDocumentTypeInfo); CopyFrom es el tipo desde el que se copian líneas.
    /// </summary>
    public sealed record Entry(string Table, string Resource, bool DefaultCanCreate, int ObjectCode, bool SupportsCancel = true,
        IReadOnlyList<ExtraFilter>? ExtraFilters = null, IReadOnlyDictionary<string, object>? CreateFields = null,
        DocumentLineType DefaultLineType = DocumentLineType.Item, bool HasTaxDocument = false, PurchaseDocumentType? CopyFrom = null);

    /// <summary>Solo para listar -- ver el doc-comment de la clase.</summary>
    public sealed record ExtraFilter(string Column, string Value, bool IsEqual);

    // ObjectCode: PurchaseOrder (22) confirmado EMPÍRICAMENTE contra Comercial GE2 real
    // (26 jul 2026, mismo criterio que SalesDocumentTypeCatalog -- se cruzó el Series de
    // Pedidos de Compra reales contra NNM1). PurchaseQuotation (23) NO tiene documentos
    // reales en GE2 para confirmar (tabla OPQT vacía en ese ambiente) -- es el código
    // estándar SAP (oPurchaseQuotations), sin confirmar empíricamente todavía. Ver
    // ISeriesCatalogService. Facturas (18) = código estándar SAP (oPurchaseInvoices),
    // pendiente de cruzar contra las series reales (SA_FAPRO) en NNM1.
    public static readonly IReadOnlyDictionary<PurchaseDocumentType, Entry> Entries = new Dictionary<PurchaseDocumentType, Entry>
    {
        [PurchaseDocumentType.PurchaseQuotation] = new("OPQT", "PurchaseQuotations", true, ObjectCode: 23, SupportsCancel: false),
        [PurchaseDocumentType.PurchaseOrder] = new("OPOR", "PurchaseOrders", true, ObjectCode: 22, SupportsCancel: false),
        // Factura de proveedores: servicios (se digita, cuenta mayor).
        [PurchaseDocumentType.PurchaseInvoice] = new("OPCH", "PurchaseInvoices", true, ObjectCode: 18,
            ExtraFilters: [new("isIns", "N", true)], DefaultLineType: DocumentLineType.Service,
            HasTaxDocument: true, CopyFrom: PurchaseDocumentType.PurchaseOrder),
        // Factura reserva de acreedores: recepción de productos (nace del Pedido; bodega
        // hace después la Entrada de mercancías contra ella).
        [PurchaseDocumentType.PurchaseReserveInvoice] = new("OPCH", "PurchaseInvoices", true, ObjectCode: 18,
            ExtraFilters: [new("isIns", "Y", true)], CreateFields: new Dictionary<string, object> { ["ReserveInvoice"] = "tYES" },
            HasTaxDocument: true, CopyFrom: PurchaseDocumentType.PurchaseOrder),
    };

    public static Entry Resolve(PurchaseDocumentType type) => Entries[type];
}
