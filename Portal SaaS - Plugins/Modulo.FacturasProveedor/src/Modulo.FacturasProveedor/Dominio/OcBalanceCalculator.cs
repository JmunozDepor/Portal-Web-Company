namespace Modulo.FacturasProveedor.Dominio;

/// <summary>Una factura que consumió saldo de una OC (total del documento, con IVA o exento).</summary>
public sealed record InvoicedAgainstOc(string SupplierTaxId, long Folio, decimal Total);

/// <summary>
/// Lo ya facturado contra una OC. La fuente principal son las facturas aceptadas de la bandeja (las cargadas por
/// DataTransfer no quedan vinculadas a la OC en SAP); SAP suma las vinculadas que no estén ya contadas. Se deduplica
/// por RUT del proveedor + folio, y si una factura está en las dos listas manda la del portal.
/// </summary>
public static class OcBalanceCalculator
{
    public static decimal AlreadyInvoiced(IReadOnlyList<InvoicedAgainstOc> portal, IReadOnlyList<InvoicedAgainstOc> sap)
    {
        var seen = new HashSet<(string, long)>();
        decimal total = 0;
        foreach (var invoice in portal.Concat(sap))
        {
            if (seen.Add((Key(invoice.SupplierTaxId), invoice.Folio))) total += invoice.Total;
        }
        return total;
    }

    private static string Key(string taxId) =>
        new(taxId.Where(c => char.IsDigit(c) || c is 'k' or 'K').Select(char.ToUpperInvariant).ToArray());
}
