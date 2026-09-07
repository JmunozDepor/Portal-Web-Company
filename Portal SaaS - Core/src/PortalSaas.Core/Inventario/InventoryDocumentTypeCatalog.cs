using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Inventario;

/// <summary>
/// Configuración por tipo de documento de inventario -- portado de
/// GenericoInventarioService._configuracion en referencia-original/PortalSAP_v2, mismo
/// patrón de catálogo estático que SalesDocumentTypeCatalog/PurchaseDocumentTypeCatalog.
/// Ninguno de los 2 tipos necesita filtros extra (a diferencia de Venta, no comparten
/// tabla/recurso entre sí) -- el campo queda por consistencia de forma con los otros
/// motores, no porque haga falta acá todavía.
///
/// OJO con el recurso de "Traslado": el bug real ya documentado en el original --
/// "InventoryTransfers" devuelve "Unrecognized resource path" en Service Layer, el
/// nombre correcto es "StockTransfers".
/// </summary>
public static class InventoryDocumentTypeCatalog
{
    /// <summary>Ver el doc-comment de SupportsCancel en SalesDocumentTypeCatalog.Entry.</summary>
    public sealed record Entry(string Table, string Resource, bool DefaultCanCreate, int ObjectCode, bool SupportsCancel = true);

    // ObjectCode: los 2 confirmados EMPÍRICAMENTE contra Comercial GE2 real (26 jul
    // 2026, mismo criterio que SalesDocumentTypeCatalog/PurchaseDocumentTypeCatalog --
    // se cruzó el Series de documentos reales de OWTQ/OWTR contra NNM1).
    // InventoryTransferRequest (1250000001) es un objeto tipo UDO/adicional, no un
    // código de 2 dígitos como el resto -- documentado acá porque no es obvio. Ver
    // ISeriesCatalogService.
    public static readonly IReadOnlyDictionary<InventoryDocumentType, Entry> Entries = new Dictionary<InventoryDocumentType, Entry>
    {
        [InventoryDocumentType.InventoryTransferRequest] = new("OWTQ", "InventoryTransferRequests", true, ObjectCode: 1250000001, SupportsCancel: false),
        [InventoryDocumentType.StockTransfer] = new("OWTR", "StockTransfers", true, ObjectCode: 67),
    };

    public static Entry Resolve(InventoryDocumentType type) => Entries[type];
}
