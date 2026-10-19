using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.SalesOrders;

/// <summary>Crear/ver una Orden de Venta -- primer inquilino del documento base genérico (ver DetailGenericSalesDocumentModelBase).</summary>
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
        ICustomerShipToAddressService shipToAddresses,
        IPaymentTermsCatalogService paymentTerms,
        IPriceListService priceLists,
        IBusinessPartnerDefaultsService partnerDefaults,
        ICurrencyCatalogService currencies)
        : base(documents, currentUser, customers, warehouses, salesEmployees, items, accounts, costCenters, series, shipToAddresses, paymentTerms, priceLists, partnerDefaults, currencies)
    {
    }

    protected override SalesDocumentType Type => SalesDocumentType.SalesOrder;
    protected override string MenuCode => "Ventas.ordenes";
    public override string DocumentName => "Orden de Venta";
    public override string RouteBase => "/ventas/ordenes";
}
