using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.CreditNotes;

/// <summary>Listado de Notas de Crédito -- segundo inquilino del documento base genérico (ver IndexGenericSalesDocumentModelBase).</summary>
public sealed class IndexModel : IndexGenericSalesDocumentModelBase
{
    public IndexModel(ISalesDocumentService documents, ICurrentUserContext currentUser)
        : base(documents, currentUser)
    {
    }

    protected override SalesDocumentType Type => SalesDocumentType.CreditNote;
    protected override string MenuCode => "Ventas.notascredito";
    public override string DocumentNamePlural => "Notas de Crédito";
    public override string RouteBase => "/ventas/notas-credito";
}
