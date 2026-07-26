using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.CustomerInvoices;

/// <summary>Ver una Factura -- de solo lectura (CanCreate=false en SalesDocumentTypeCatalog, mismo criterio que la referencia).</summary>
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

    protected override SalesDocumentType Type => SalesDocumentType.CustomerInvoice;
    protected override string MenuCode => "Ventas.facturas";
    public override string DocumentName => "Factura";
    public override string RouteBase => "/ventas/facturas";
}
