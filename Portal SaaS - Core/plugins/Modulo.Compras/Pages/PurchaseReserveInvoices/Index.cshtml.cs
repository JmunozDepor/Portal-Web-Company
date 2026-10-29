using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Compras.Pages.PurchaseReserveInvoices;

/// <summary>Listado de Facturas Reserva de Proveedor -- ver IndexGenericPurchaseDocumentModelBase.</summary>
public sealed class IndexModel : IndexGenericPurchaseDocumentModelBase
{
    public IndexModel(IPurchaseDocumentService documents, ICurrentUserContext currentUser)
        : base(documents, currentUser)
    {
    }

    protected override PurchaseDocumentType Type => PurchaseDocumentType.PurchaseReserveInvoice;
    protected override string MenuCode => "Compras.facturasreserva";
    public override string DocumentNamePlural => "Facturas Reserva de Proveedor";
    public override string RouteBase => "/compras/facturas-reserva";
}
