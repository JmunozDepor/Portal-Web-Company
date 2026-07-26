using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.Receipts;

/// <summary>Ver una Boleta -- de solo lectura (CanCreate=false en SalesDocumentTypeCatalog, mismo criterio que la referencia). Mismo diccionario base "OINV"/"Invoices" que Facturas, se distingue por isIns=N + DocSubType=IB.</summary>
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

    protected override SalesDocumentType Type => SalesDocumentType.Receipt;
    protected override string MenuCode => "Ventas.boletas";
    public override string DocumentName => "Boleta";
    public override string RouteBase => "/ventas/boletas";
}
