using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Compras.Pages.PurchaseInvoices;

/// <summary>Crear/ver una Factura de Proveedor (servicios) -- "OPCH"/"PurchaseInvoices" con isIns=N, ver PurchaseDocumentTypeCatalog. Todo lo demás lo hereda de DetailGenericPurchaseDocumentModelBase.</summary>
public sealed class DetailModel : DetailGenericPurchaseDocumentModelBase
{
    public DetailModel(
        IPurchaseDocumentService documents,
        ICurrentUserContext currentUser,
        ISupplierCatalogService suppliers,
        IWarehouseCatalogService warehouses,
        IItemCatalogService items,
        IGeneralLedgerAccountCatalogService accounts,
        ICostCenterCatalogService costCenters,
        ISeriesCatalogService series,
        ISalesEmployeeCatalogService buyers,
        IPriceListService priceLists,
        IBusinessPartnerDefaultsService partnerDefaults,
        ICurrencyCatalogService currencies)
        : base(documents, currentUser, suppliers, warehouses, items, accounts, costCenters, series, buyers, priceLists, partnerDefaults, currencies)
    {
    }

    protected override PurchaseDocumentType Type => PurchaseDocumentType.PurchaseInvoice;
    protected override string MenuCode => "Compras.facturas";
    public override string DocumentName => "Factura de Proveedor";
    public override string RouteBase => "/compras/facturas-proveedor";
}
