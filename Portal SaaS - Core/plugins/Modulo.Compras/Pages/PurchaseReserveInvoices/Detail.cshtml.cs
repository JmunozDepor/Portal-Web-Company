using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Compras.Pages.PurchaseReserveInvoices;

/// <summary>Crear/ver una Factura Reserva de Proveedor (recepción de productos) -- mismo "OPCH"/"PurchaseInvoices" que Facturas de Proveedor, se distingue por isIns=Y al listar y ReserveInvoice=tYES al crear (ver PurchaseDocumentTypeCatalog). Carpeta PurchaseReserveInvoices, no ReserveInvoices: Modulo.Ventas ya tiene esa y Razor las pisaría.</summary>
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

    protected override PurchaseDocumentType Type => PurchaseDocumentType.PurchaseReserveInvoice;
    protected override string MenuCode => "Compras.facturasreserva";
    public override string DocumentName => "Factura Reserva de Proveedor";
    public override string RouteBase => "/compras/facturas-reserva";
}
