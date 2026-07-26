using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Compras.Pages.PurchaseOrders;

/// <summary>
/// Crear/ver un Pedido -- segundo inquilino del documento base genérico (ver
/// DetailGenericPurchaseDocumentModelBase). A diferencia del original (nace de
/// Copy-From al aprobar una Oferta), acá se crea directo -- ver CLAUDE.md.
/// </summary>
public sealed class DetailModel : DetailGenericPurchaseDocumentModelBase
{
    public DetailModel(
        IPurchaseDocumentService documents,
        ICurrentUserContext currentUser,
        ISupplierCatalogService suppliers,
        IWarehouseCatalogService warehouses,
        IItemCatalogService items)
        : base(documents, currentUser, suppliers, warehouses, items)
    {
    }

    protected override PurchaseDocumentType Type => PurchaseDocumentType.PurchaseOrder;
    protected override string MenuCode => "Compras.pedidos";
    public override string DocumentName => "Pedido";
    public override string RouteBase => "/compras/pedidos";
}
