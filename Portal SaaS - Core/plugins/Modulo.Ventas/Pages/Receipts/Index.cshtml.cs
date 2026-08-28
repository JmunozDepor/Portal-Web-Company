using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.Receipts;

/// <summary>Listado de Boletas -- de solo lectura (CanCreate=false en SalesDocumentTypeCatalog, mismo criterio que la referencia). Mismo diccionario base "OINV"/"Invoices" que Facturas, se distingue por isIns=N + DocSubType=IB.</summary>
public sealed class IndexModel : IndexGenericSalesDocumentModelBase
{
    public IndexModel(ISalesDocumentService documents, ICurrentUserContext currentUser)
        : base(documents, currentUser)
    {
    }

    protected override SalesDocumentType Type => SalesDocumentType.Receipt;
    protected override string MenuCode => "Ventas.boletas";
    public override string DocumentNamePlural => "Boletas";
    public override string RouteBase => "/ventas/boletas";
}
