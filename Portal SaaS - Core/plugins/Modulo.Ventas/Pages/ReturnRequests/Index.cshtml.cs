using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.ReturnRequests;

/// <summary>Listado de Solicitudes de Devolución -- de solo lectura (CanCreate=false en SalesDocumentTypeCatalog, mismo criterio que la referencia).</summary>
public sealed class IndexModel : IndexGenericSalesDocumentModelBase
{
    public IndexModel(ISalesDocumentService documents, ICurrentUserContext currentUser)
        : base(documents, currentUser)
    {
    }

    protected override SalesDocumentType Type => SalesDocumentType.ReturnRequest;
    protected override string MenuCode => "Ventas.solicitudesdevolucion";
    public override string DocumentNamePlural => "Solicitudes de Devolución";
    public override string RouteBase => "/ventas/solicitudes-devolucion";
}
