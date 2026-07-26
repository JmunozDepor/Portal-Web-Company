using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.ReturnRequests;

/// <summary>Ver una Solicitud de Devolución -- de solo lectura (CanCreate=false en SalesDocumentTypeCatalog, mismo criterio que la referencia).</summary>
public sealed class DetailModel : DetailGenericSalesDocumentModelBase
{
    public DetailModel(
        ISalesDocumentService documents,
        ICurrentUserContext currentUser,
        ICustomerCatalogService customers,
        IWarehouseCatalogService warehouses,
        ISalesEmployeeCatalogService salesEmployees,
        IItemCatalogService items)
        : base(documents, currentUser, customers, warehouses, salesEmployees, items)
    {
    }

    protected override SalesDocumentType Type => SalesDocumentType.ReturnRequest;
    protected override string MenuCode => "Ventas.solicitudesdevolucion";
    public override string DocumentName => "Solicitud de Devolución";
    public override string RouteBase => "/ventas/solicitudes-devolucion";
}
