using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Inventario.Pages.StockTransfers;

/// <summary>Listado de Traslados -- segundo inquilino del documento base genérico (ver IndexGenericInventoryDocumentModelBase).</summary>
public sealed class IndexModel : IndexGenericInventoryDocumentModelBase
{
    public IndexModel(IInventoryDocumentService documents, ICurrentUserContext currentUser)
        : base(documents, currentUser)
    {
    }

    protected override InventoryDocumentType Type => InventoryDocumentType.StockTransfer;
    protected override string MenuCode => "Inventario.traslados";
    public override string DocumentNamePlural => "Traslados";
    public override string RouteBase => "/inventario/traslados";
}
