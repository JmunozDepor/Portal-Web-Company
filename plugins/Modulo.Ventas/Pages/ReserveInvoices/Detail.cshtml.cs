using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.ReserveInvoices;

/// <summary>Crear/ver una Factura de Reserva -- mismo diccionario base "OINV"/"Invoices" que Facturas, se distingue por el filtro isIns=Y del catálogo.</summary>
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

    protected override SalesDocumentType Type => SalesDocumentType.ReserveInvoice;
    protected override string MenuCode => "Ventas.facturasreserva";
    public override string DocumentName => "Factura de Reserva";
    public override string RouteBase => "/ventas/facturas-reserva";
}
