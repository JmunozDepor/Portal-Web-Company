using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Inventario.Pages.InventoryTransferRequests;

/// <summary>Listado de Solicitudes de Traslado -- primer inquilino del documento base genérico (ver IndexGenericInventoryDocumentModelBase).</summary>
public sealed class IndexModel : IndexGenericInventoryDocumentModelBase
{
    public IndexModel(IInventoryDocumentService documents, ICurrentUserContext currentUser)
        : base(documents, currentUser)
    {
    }

    protected override InventoryDocumentType Type => InventoryDocumentType.InventoryTransferRequest;
    protected override string MenuCode => "Inventario.solicitudestraslado";
    public override string DocumentNamePlural => "Solicitudes de Traslado";
    public override string RouteBase => "/inventario/solicitudes-traslado";
}
