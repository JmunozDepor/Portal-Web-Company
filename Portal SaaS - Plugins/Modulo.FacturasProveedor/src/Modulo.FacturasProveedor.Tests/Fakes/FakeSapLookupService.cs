using Modulo.FacturasProveedor.Dominio;
using Modulo.FacturasProveedor.Servicios;

namespace Modulo.FacturasProveedor.Tests.Fakes;

/// <summary>SAP en memoria: OC por DocNum, facturado por DocEntry y proveedores por RUT normalizado.</summary>
public sealed class FakeSapLookupService : ISapLookupService
{
    public Dictionary<int, List<SapPurchaseOrder>> PurchaseOrders { get; } = new();
    public Dictionary<int, List<InvoicedAgainstOc>> Invoiced { get; } = new();
    public Dictionary<string, SapSupplier> Suppliers { get; } = new();
    public int PurchaseOrderQueries { get; private set; }

    public FakeSapLookupService AddSupplier(string taxId = "76412553-1", string cardCode = "P76412553", string name = "Comercial Textil Andes SpA", bool active = true)
    {
        Suppliers[SapLookupService.NormalizeRut(taxId)!] = new SapSupplier(cardCode, name, SapLookupService.NormalizeRut(taxId)!, active);
        return this;
    }

    public FakeSapLookupService AddPurchaseOrder(int docNum = 4512, decimal total = 3_200_000, DateOnly? docDate = null,
        string taxId = "76412553-1", string cardCode = "P76412553", string status = "open", string series = "Primario", int? docEntry = null)
    {
        if (!PurchaseOrders.TryGetValue(docNum, out var list)) PurchaseOrders[docNum] = list = new();
        list.Add(new SapPurchaseOrder(docEntry ?? 100 + docNum + list.Count * 1000, docNum, series, cardCode, taxId,
            docDate ?? new DateOnly(2026, 10, 12), total, "CLP", status));
        return this;
    }

    public Task<IReadOnlyList<SapPurchaseOrder>> FindPurchaseOrdersAsync(int docNum, CancellationToken ct)
    {
        PurchaseOrderQueries++;
        return Task.FromResult<IReadOnlyList<SapPurchaseOrder>>(PurchaseOrders.TryGetValue(docNum, out var l) ? l : new List<SapPurchaseOrder>());
    }

    public Task<IReadOnlyList<InvoicedAgainstOc>> FindInvoicedAsync(int docEntry, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<InvoicedAgainstOc>>(Invoiced.TryGetValue(docEntry, out var l) ? l : new List<InvoicedAgainstOc>());

    /// <summary>RUT cuya búsqueda de proveedor lanza (simula SAP caído o una consulta desactivada).</summary>
    public HashSet<string> FailingTaxIds { get; } = new();

    public Task<SapSupplier?> FindSupplierByTaxIdAsync(string taxId, CancellationToken ct) =>
        FailingTaxIds.Contains(taxId)
            ? throw new InvalidOperationException("SAP no responde.")
            : Task.FromResult(Suppliers.TryGetValue(SapLookupService.NormalizeRut(taxId) ?? "", out var s) ? s : null);

    public Task<IReadOnlyList<SapSupplier>> SearchSuppliersAsync(string text, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SapSupplier>>(Suppliers.Values
            .Where(s => s.CardName.Contains(text, StringComparison.OrdinalIgnoreCase) || s.TaxId.Contains(text)).ToList());

    public Task<IReadOnlyList<SapQueryDto>> ListQueriesAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task SaveQueryAsync(string kind, string sql, bool isActive, CancellationToken ct) => throw new NotSupportedException();
    public Task<QueryValidationResult> TestQueryAsync(string kind, string sql, string sampleValue, CancellationToken ct) => throw new NotSupportedException();
}
