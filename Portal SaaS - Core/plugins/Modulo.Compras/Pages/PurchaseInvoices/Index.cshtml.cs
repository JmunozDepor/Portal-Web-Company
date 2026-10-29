using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Compras.Pages.PurchaseInvoices;

/// <summary>Listado de Facturas de Proveedor -- ver IndexGenericPurchaseDocumentModelBase.</summary>
public sealed class IndexModel : IndexGenericPurchaseDocumentModelBase
{
    public IndexModel(IPurchaseDocumentService documents, ICurrentUserContext currentUser)
        : base(documents, currentUser)
    {
    }

    protected override PurchaseDocumentType Type => PurchaseDocumentType.PurchaseInvoice;
    protected override string MenuCode => "Compras.facturas";
    public override string DocumentNamePlural => "Facturas de Proveedor";
    public override string RouteBase => "/compras/facturas-proveedor";
}
