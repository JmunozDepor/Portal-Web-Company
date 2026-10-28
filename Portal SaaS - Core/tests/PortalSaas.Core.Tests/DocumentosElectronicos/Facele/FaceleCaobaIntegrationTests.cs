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

    /// <summary>
    /// Recepción (solo lectura): además de FACELE_USER / FACELE_PASS exige FACELE_USER_MAIL y FACELE_RUT_RECEPTOR.
    /// Nunca responde documentos: aprobarDTERecibidos no se prueba aquí.
    /// </summary>
    private static DteReceptionContext? ReceptionCtx()
    {
        var user = Environment.GetEnvironmentVariable("FACELE_USER");
        var pass = Environment.GetEnvironmentVariable("FACELE_PASS");
        var mail = Environment.GetEnvironmentVariable("FACELE_USER_MAIL");
        var rut = Environment.GetEnvironmentVariable("FACELE_RUT_RECEPTOR");
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass) || string.IsNullOrEmpty(mail) || string.IsNullOrEmpty(rut))
            return null;
        return new DteReceptionContext("https://caoba.docele.cl/cl-dol", user, pass, rut, mail, "certification");
    }

    [Fact]
    public async Task Caoba_ConsultarRecepcion_Y_Referencias()
    {
        if (ReceptionCtx() is not { } ctx) return;
        var provider = new FaceleReceptionProvider(new FaceleSoapClient(new HttpClient()));

        await provider.TestAsync(ctx, CancellationToken.None);
        // Un año: caoba guarda copias de registros del SII de meses atrás (2026-10-28: 273 de abril-mayo).
        var today = DateOnly.FromDateTime(DateTime.Today);
        var received = await provider.ListReceivedAsync(ctx, today.AddDays(-365), today, CancellationToken.None);

        Assert.All(received, r =>
        {
            Assert.False(string.IsNullOrEmpty(r.IssuerTaxId));
            Assert.True(r.DteType > 0 && r.Folio > 0);
            Assert.NotNull(r.SiiReceivedAt);
            Assert.Equal(r.ClaimStatus is "Documento_Pagado_Al_Contado" or "No_Aplica", r.NotRespondableReason is not null);
        });
        if (received.FirstOrDefault() is { } first)
            await provider.GetReferencesAsync(ctx, first.IssuerTaxId, first.DteType, first.Folio, CancellationToken.None);
    }
}
