using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Inventario.Pages.InventoryTransferRequests;

/// <summary>Crear/ver una Solicitud de Traslado -- primer inquilino del documento base genérico (ver DetailGenericInventoryDocumentModelBase).</summary>
public sealed class DetailModel : DetailGenericInventoryDocumentModelBase
{
    public DetailModel(
        IInventoryDocumentService documents,
        ICurrentUserContext currentUser,
        IWarehouseCatalogService warehouses,
        IItemCatalogService items,
        ISeriesCatalogService series,
        ICustomerCatalogService customers)
        : base(documents, currentUser, warehouses, items, series, customers)
    {
    }

    protected override InventoryDocumentType Type => InventoryDocumentType.InventoryTransferRequest;
    protected override string MenuCode => "Inventario.solicitudestraslado";
    public override string DocumentName => "Solicitud de Traslado";
    public override string RouteBase => "/inventario/solicitudes-traslado";
}
