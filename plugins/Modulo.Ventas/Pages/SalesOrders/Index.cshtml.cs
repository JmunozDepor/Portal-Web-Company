using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.SalesOrders;

/// <summary>Listado de Órdenes de Venta -- primer inquilino del documento base genérico (ver IndexGenericSalesDocumentModelBase).</summary>
public sealed class IndexModel : IndexGenericSalesDocumentModelBase
{
    public IndexModel(ISalesDocumentService documents, ICurrentUserContext currentUser)
        : base(documents, currentUser)
    {
    }

    protected override SalesDocumentType Type => SalesDocumentType.SalesOrder;
    protected override string MenuCode => "Ventas.ordenes";
    public override string DocumentNamePlural => "Órdenes de Venta";
    public override string RouteBase => "/ventas/ordenes";
}
