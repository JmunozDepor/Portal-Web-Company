using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Compras.Pages.PurchaseOrders;

/// <summary>Listado de Pedidos -- segundo inquilino del documento base genérico (ver IndexGenericPurchaseDocumentModelBase).</summary>
public sealed class IndexModel : IndexGenericPurchaseDocumentModelBase
{
    public IndexModel(IPurchaseDocumentService documents, ICurrentUserContext currentUser)
        : base(documents, currentUser)
    {
    }

    protected override PurchaseDocumentType Type => PurchaseDocumentType.PurchaseOrder;
    protected override string MenuCode => "Compras.pedidos";
    public override string DocumentNamePlural => "Pedidos";
    public override string RouteBase => "/compras/pedidos";
}
