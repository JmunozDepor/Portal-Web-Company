using System.Text;
using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Core.DocumentosElectronicos;
using PortalSaas.Core.DocumentosElectronicos.Proveedores.Facele;

namespace PortalSaas.Core.Tests.DocumentosElectronicos.Facele;

/// <summary>
/// Contra el ambiente de certificación de FACELE (caoba). Solo corre con las variables de entorno
/// FACELE_USER / FACELE_PASS (nunca en archivos); sin ellas no hace nada. Solo pide un borrador:
/// no emite ni consume folio.
/// </summary>
public class FaceleCaobaIntegrationTests
{
    [Fact]
    public async Task Caoba_BorradorGuia52_DevuelvePdf()
    {
        var user = Environment.GetEnvironmentVariable("FACELE_USER");
        var pass = Environment.GetEnvironmentVariable("FACELE_PASS");
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass)) return;

        var issuer = new DteIssuerData("76030680-0", "COMERCIAL DEPOR LIMITADA", "IMPORTACION, EXPORTACION, COMERCIALIZACION",
            464100, "CAMINO SAN ESTEBAN 1361", "SAN BERNARDO", "SANTIAGO", null);
        var doc = new DteDocument(
            new DteIdDoc(52, DateOnly.FromDateTime(DateTime.Today), 1, 2), null,
            new DteReceiver("78454830-9", "Receptor de prueba", "Comercio", "Lira 200", "Santiago", "Santiago"),
            new DteTransport("PJFX11", null, "74564934-3", new DteDriver("17786044-1", "JOSE PEREZ"), "Lira 200", "Santiago",
                "Santiago", null, DateOnly.FromDateTime(DateTime.Today), new TimeOnly(8, 30), DateOnly.FromDateTime(DateTime.Today)),
            null, new[] { new DteLine(1, "A1", "Articulo prueba", null, 1, "UN", null, 0) },
            Array.Empty<DteReference>(), Array.Empty<DteExtraField>());

        var provider = new FaceleDteProvider(new FaceleSoapClient(new HttpClient()));
        var ctx = new DteProviderContext("https://caoba.docele.cl/cl-dol-auth", user, pass, issuer.TaxId, "certification");
        var pdf = await provider.PreviewAsync(ctx, provider.BuildXml(doc, issuer), 52, CancellationToken.None);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
