using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.CustomerInvoices;

/// <summary>Listado de Facturas -- de solo lectura (CanCreate=false en SalesDocumentTypeCatalog, mismo criterio que la referencia).</summary>
public sealed class IndexModel : IndexGenericSalesDocumentModelBase
{
    public IndexModel(ISalesDocumentService documents, ICurrentUserContext currentUser)
        : base(documents, currentUser)
    {
    }

    protected override SalesDocumentType Type => SalesDocumentType.CustomerInvoice;
    protected override string MenuCode => "Ventas.facturas";
    public override string DocumentNamePlural => "Facturas";
    public override string RouteBase => "/ventas/facturas";
}
