using Modulo.FacturasProveedor.Dominio;
using Modulo.FacturasProveedor.Dominio.Reglas;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.FacturasProveedor.Servicios;

/// <summary>
/// Arma el InvoiceEvaluationContext de cada factura de un ciclo, con caché de SAP (OC, facturado por OC, proveedor) y
/// el registro de lo ya aceptado contra cada OC. Lo aceptado en el mismo ciclo se agrega con MarkAccepted, así dos
/// parciales de la misma OC no se aceptan las dos si juntas superan el total.
/// </summary>
internal sealed class ReviewContextBuilder
{
    private readonly ISapLookupService _sap;
    private readonly ISupplierProfileService _profiles;
    private readonly Dictionary<int, IReadOnlyList<SapPurchaseOrder>> _purchaseOrders = new();
    private readonly Dictionary<int, IReadOnlyList<InvoicedAgainstOc>> _sapInvoiced = new();
    private readonly Dictionary<string, SapSupplier?> _suppliers = new();
    private readonly Dictionary<long, DteReceivedDto> _accepted = new();

    public ReviewContextBuilder(ISapLookupService sap, ISupplierProfileService profiles, IEnumerable<DteReceivedDto> alreadyAccepted)
    {
        _sap = sap;
        _profiles = profiles;
        foreach (var doc in alreadyAccepted) _accepted[doc.Id] = doc;
    }

    public void MarkAccepted(DteReceivedDto doc) => _accepted[doc.Id] = doc;

    public async Task<InvoiceEvaluationContext> BuildAsync(DteReceivedDto doc, DateOnly siiReceivedOn, CancellationToken ct)
    {
        var docNums = doc.PurchaseOrderRefs.Select(r => r.DocNum).Distinct().ToList();
        var resolved = new List<PurchaseOrderFacts>();
        var unresolved = new List<int>();
        var ambiguous = new List<int>();
        foreach (var docNum in docNums)
        {
            var found = await PurchaseOrdersAsync(docNum, ct);
            if (found.Count == 0) { unresolved.Add(docNum); continue; }
            if (found.Count > 1) { ambiguous.Add(docNum); continue; }
            var po = found[0];
            var portal = _accepted.Values
                .Where(d => d.Id != doc.Id && d.PurchaseOrderRefs.Any(r => r.DocNum == docNum))
                .Select(d => new InvoicedAgainstOc(d.IssuerTaxId, d.Folio, d.TotalAmount))
                .ToList();
            var sapInvoiced = (await SapInvoicedAsync(po.DocEntry, ct))
                .Where(i => !(SameRut(i.SupplierTaxId, doc.IssuerTaxId) && i.Folio == doc.Folio))
                .ToList();
            resolved.Add(new PurchaseOrderFacts(po.DocNum, po.DocEntry, po.Series, po.CardCode, po.SupplierTaxId, po.DocDate,
                po.TotalLocal, po.Status, OcBalanceCalculator.AlreadyInvoiced(portal, sapInvoiced)));
        }

        var supplier = await SupplierAsync(doc.IssuerTaxId, ct);
        var profile = await _profiles.GetActiveFactsAsync(doc.IssuerTaxId, siiReceivedOn, ct);
        var invoice = new InvoiceFacts(doc.Id, doc.IssuerTaxId, doc.DteType, doc.Folio, doc.IssueDate, siiReceivedOn, doc.TotalAmount, docNums);
        return new InvoiceEvaluationContext(
            invoice, new SupplierFacts(supplier is not null, supplier?.IsActive ?? false, supplier?.CardCode, profile),
            resolved, unresolved, ambiguous);
    }

    private async Task<IReadOnlyList<SapPurchaseOrder>> PurchaseOrdersAsync(int docNum, CancellationToken ct)
    {
        if (!_purchaseOrders.TryGetValue(docNum, out var list))
            _purchaseOrders[docNum] = list = await _sap.FindPurchaseOrdersAsync(docNum, ct);
        return list;
    }

    private async Task<IReadOnlyList<InvoicedAgainstOc>> SapInvoicedAsync(int docEntry, CancellationToken ct)
    {
        if (!_sapInvoiced.TryGetValue(docEntry, out var list))
            _sapInvoiced[docEntry] = list = await _sap.FindInvoicedAsync(docEntry, ct);
        return list;
    }

    private async Task<SapSupplier?> SupplierAsync(string taxId, CancellationToken ct)
    {
        var key = SapLookupService.NormalizeRut(taxId) ?? taxId;
        if (!_suppliers.TryGetValue(key, out var supplier))
            _suppliers[key] = supplier = await _sap.FindSupplierByTaxIdAsync(taxId, ct);
        return supplier;
    }

    private static bool SameRut(string a, string b) => SapLookupService.NormalizeRut(a) == SapLookupService.NormalizeRut(b);
}
