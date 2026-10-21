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
