using Modulo.Despachos.Models;

namespace Modulo.Despachos.Servicios;

/// <summary>Consultas SAP de ejemplo (HANA) embebidas en el ensamblado; se cargan al primer uso de la compañía.</summary>
public static class SapQuerySeeds
{
    public static string Get(string documentKind)
    {
        var suffix = documentKind switch
        {
            DocumentKinds.Invoice => "hana_invoice.sql",
            DocumentKinds.TransferGuide => "hana_transfer_guide.sql",
            SapQueryKinds.InvoiceLines => "hana_invoice_lines.sql",
            SapQueryKinds.TransferByLpn => "hana_transfer_by_lpn.sql",
            SapQueryKinds.TransferPending => "hana_transfer_pending.sql",
            SapQueryKinds.TransferPendingAll => "hana_transfer_pending_all.sql",
            SapQueryKinds.TransferLines => "hana_transfer_lines.sql",
            SapQueryKinds.Warehouses => "hana_warehouses.sql",
            SapQueryKinds.BusinessPartnerByTaxId => "hana_business_partner_by_tax_id.sql",
            _ => throw new InvalidOperationException($"Tipo de documento desconocido: '{documentKind}'."),
        };
        var assembly = typeof(SapQuerySeeds).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd().Trim();
    }
}
