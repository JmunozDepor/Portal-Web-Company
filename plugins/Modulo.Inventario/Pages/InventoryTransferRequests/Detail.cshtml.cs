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
        IItemCatalogService items)
        : base(documents, currentUser, warehouses, items)
    {
    }

    protected override InventoryDocumentType Type => InventoryDocumentType.InventoryTransferRequest;
    protected override string MenuCode => "Inventario.solicitudestraslado";
    public override string DocumentName => "Solicitud de Traslado";
    public override string RouteBase => "/inventario/solicitudes-traslado";
}
