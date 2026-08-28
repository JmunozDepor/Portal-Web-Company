using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Compras.Pages.PurchaseQuotations;

/// <summary>Listado de Ofertas de Compra -- primer inquilino del documento base genérico (ver IndexGenericPurchaseDocumentModelBase).</summary>
public sealed class IndexModel : IndexGenericPurchaseDocumentModelBase
{
    public IndexModel(IPurchaseDocumentService documents, ICurrentUserContext currentUser)
        : base(documents, currentUser)
    {
    }

    protected override PurchaseDocumentType Type => PurchaseDocumentType.PurchaseQuotation;
    protected override string MenuCode => "Compras.ofertas";
    public override string DocumentNamePlural => "Ofertas de Compra";
    public override string RouteBase => "/compras/ofertas";
}
