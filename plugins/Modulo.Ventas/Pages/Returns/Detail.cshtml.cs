using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.Returns;

/// <summary>Ver una Devolución -- de solo lectura (CanCreate=false en SalesDocumentTypeCatalog, mismo criterio que la referencia).</summary>
public sealed class DetailModel : DetailGenericSalesDocumentModelBase
{
    public DetailModel(
        ISalesDocumentService documents,
        ICurrentUserContext currentUser,
        ICustomerCatalogService customers,
        IWarehouseCatalogService warehouses,
        ISalesEmployeeCatalogService salesEmployees,
        IItemCatalogService items,
        IGeneralLedgerAccountCatalogService accounts,
        ICostCenterCatalogService costCenters,
        ISeriesCatalogService series,
        IShippingMethodCatalogService shippingMethods,
        IPaymentTermsCatalogService paymentTerms)
        : base(documents, currentUser, customers, warehouses, salesEmployees, items, accounts, costCenters, series, shippingMethods, paymentTerms)
    {
    }

    protected override SalesDocumentType Type => SalesDocumentType.Return;
    protected override string MenuCode => "Ventas.devoluciones";
    public override string DocumentName => "Devolución";
    public override string RouteBase => "/ventas/devoluciones";
}
