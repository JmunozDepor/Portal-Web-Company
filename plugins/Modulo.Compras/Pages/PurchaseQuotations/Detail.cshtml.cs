using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Compras.Pages.PurchaseQuotations;

/// <summary>Crear/ver una Oferta de Compra -- primer inquilino del documento base genérico (ver DetailGenericPurchaseDocumentModelBase).</summary>
public sealed class DetailModel : DetailGenericPurchaseDocumentModelBase
{
    public DetailModel(
        IPurchaseDocumentService documents,
        ICurrentUserContext currentUser,
        ISupplierCatalogService suppliers,
        IWarehouseCatalogService warehouses,
        IItemCatalogService items,
        IGeneralLedgerAccountCatalogService accounts,
        ICostCenterCatalogService costCenters,
        ISeriesCatalogService series)
        : base(documents, currentUser, suppliers, warehouses, items, accounts, costCenters, series)
    {
    }

    protected override PurchaseDocumentType Type => PurchaseDocumentType.PurchaseQuotation;
    protected override string MenuCode => "Compras.ofertas";
    public override string DocumentName => "Oferta de Compra";
    public override string RouteBase => "/compras/ofertas";
}
