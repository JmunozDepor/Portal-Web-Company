using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.Returns;

/// <summary>Listado de Devoluciones -- de solo lectura (CanCreate=false en SalesDocumentTypeCatalog, mismo criterio que la referencia).</summary>
public sealed class IndexModel : IndexGenericSalesDocumentModelBase
{
    public IndexModel(ISalesDocumentService documents, ICurrentUserContext currentUser)
        : base(documents, currentUser)
    {
    }

    protected override SalesDocumentType Type => SalesDocumentType.Return;
    protected override string MenuCode => "Ventas.devoluciones";
    public override string DocumentNamePlural => "Devoluciones";
    public override string RouteBase => "/ventas/devoluciones";
}
