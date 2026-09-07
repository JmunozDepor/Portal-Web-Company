using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Compras;

/// <summary>
/// Configuración por tipo de documento de compra -- portado de
/// GenericoCompraCatalogo en referencia-original/PortalSAP_v2, mismo patrón de
/// catálogo estático que SalesDocumentTypeCatalog/InventoryDocumentTypeCatalog.
/// Ninguno de los 2 tipos comparte tabla/recurso entre sí (a diferencia de Venta) --
/// sin campo de filtros extra acá, mismo criterio que InventoryDocumentTypeCatalog.
/// Sin BaseType (Copy-From): esta entrega crea los 2 tipos por digitación directa, sin
/// el flujo de aprobación que en el original generaba el Pedido a partir de la Oferta
/// -- ver CLAUDE.md.
/// </summary>
public static class PurchaseDocumentTypeCatalog
{
    /// <summary>Ver el doc-comment de SupportsCancel en SalesDocumentTypeCatalog.Entry.</summary>
    public sealed record Entry(string Table, string Resource, bool DefaultCanCreate, int ObjectCode, bool SupportsCancel = true);

    // ObjectCode: PurchaseOrder (22) confirmado EMPÍRICAMENTE contra Comercial GE2 real
    // (26 jul 2026, mismo criterio que SalesDocumentTypeCatalog -- se cruzó el Series de
    // Pedidos de Compra reales contra NNM1). PurchaseQuotation (23) NO tiene documentos
    // reales en GE2 para confirmar (tabla OPQT vacía en ese ambiente) -- es el código
    // estándar SAP (oPurchaseQuotations), sin confirmar empíricamente todavía. Ver
    // ISeriesCatalogService.
    public static readonly IReadOnlyDictionary<PurchaseDocumentType, Entry> Entries = new Dictionary<PurchaseDocumentType, Entry>
    {
        [PurchaseDocumentType.PurchaseQuotation] = new("OPQT", "PurchaseQuotations", true, ObjectCode: 23, SupportsCancel: false),
        [PurchaseDocumentType.PurchaseOrder] = new("OPOR", "PurchaseOrders", true, ObjectCode: 22, SupportsCancel: false),
    };

    public static Entry Resolve(PurchaseDocumentType type) => Entries[type];
}
