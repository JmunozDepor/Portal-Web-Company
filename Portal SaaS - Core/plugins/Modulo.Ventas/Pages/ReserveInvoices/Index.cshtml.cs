using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.ReserveInvoices;

/// <summary>Listado de Facturas de Reserva -- mismo diccionario base "OINV"/"Invoices" que Facturas, se distingue por el filtro isIns=Y del catálogo.</summary>
public sealed class IndexModel : IndexGenericSalesDocumentModelBase
{
    public IndexModel(ISalesDocumentService documents, ICurrentUserContext currentUser)
        : base(documents, currentUser)
    {
    }

    protected override SalesDocumentType Type => SalesDocumentType.ReserveInvoice;
    protected override string MenuCode => "Ventas.facturasreserva";
    public override string DocumentNamePlural => "Facturas de Reserva";
    public override string RouteBase => "/ventas/facturas-reserva";
}
