using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Inventario.Pages.StockTransfers;

/// <summary>Crear/ver un Traslado -- segundo inquilino del documento base genérico (ver DetailGenericInventoryDocumentModelBase).</summary>
public sealed class DetailModel : DetailGenericInventoryDocumentModelBase
{
    public DetailModel(
        IInventoryDocumentService documents,
        ICurrentUserContext currentUser,
        IWarehouseCatalogService warehouses,
        IItemCatalogService items)
        : base(documents, currentUser, warehouses, items)
    {
    }

    protected override InventoryDocumentType Type => InventoryDocumentType.StockTransfer;
    protected override string MenuCode => "Inventario.traslados";
    public override string DocumentName => "Traslado";
    public override string RouteBase => "/inventario/traslados";
}
