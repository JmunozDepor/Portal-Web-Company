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
    public sealed record Entry(string Table, string Resource, bool DefaultCanCreate);

    public static readonly IReadOnlyDictionary<InventoryDocumentType, Entry> Entries = new Dictionary<InventoryDocumentType, Entry>
    {
        [InventoryDocumentType.InventoryTransferRequest] = new("OWTQ", "InventoryTransferRequests", true),
        [InventoryDocumentType.StockTransfer] = new("OWTR", "StockTransfers", true),
    };

    public static Entry Resolve(InventoryDocumentType type) => Entries[type];
}
