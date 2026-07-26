using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.CreditNotes;

/// <summary>Crear/ver una Nota de Crédito -- segundo inquilino del documento base genérico (ver DetailGenericSalesDocumentModelBase).</summary>
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

    protected override SalesDocumentType Type => SalesDocumentType.CreditNote;
    protected override string MenuCode => "Ventas.notascredito";
    public override string DocumentName => "Nota de Crédito";
    public override string RouteBase => "/ventas/notas-credito";
}
