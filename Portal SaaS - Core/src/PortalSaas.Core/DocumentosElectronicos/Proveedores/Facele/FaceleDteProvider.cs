using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PortalSaas.Abstractions.Modelos.Dte;

namespace PortalSaas.Core.DocumentosElectronicos.Proveedores.Facele;

/// <summary>
/// Adaptador FACELE (DoceleOL). Emisión en dos fases: generaDTE asigna el folio (uuid = EmissionKey,
/// FACELE rechaza un uuid repetido indicando el folio original) y obtieneDTE confirma. Protocolo y
/// pruebas en caoba: Modulo.Despachos/docs/superpowers/specs/2026-10-22-modulo-despachos-etapa2-guias-res154-design.md §5.3b.
/// </summary>
public sealed partial class FaceleDteProvider : IDteProvider
{
    private readonly FaceleSoapClient _soap;

    public FaceleDteProvider(FaceleSoapClient soap) => _soap = soap;

    public string Code => "FACELE";

    public DteProviderCapabilities Capabilities { get; } =
        new(new[] { 33, 34, 52, 56, 61 }, TwoPhase: true, SupportsPreview: true, SupportsRelease: true);

    public string BuildXml(DteDocument doc, DteIssuerData issuer) => FaceleXmlBuilder.Build(doc, issuer);

    public async Task<byte[]> PreviewAsync(DteProviderContext ctx, string xml, int dteType, CancellationToken ct)
    {
        // formato = formato de ENTRADA (igual que generaDTE); la respuesta trae el PDF borrador.
        var r = await CallAsync(ctx, "obtieneBorrador", ct,
            ("rutEmisor", ctx.IssuerTaxId), ("tipoDTE", Int(dteType)), ("formato", "XML"), ("xml", xml));
        EnsureOk(r, "obtener el borrador");
        return DecodePdf(r);
    }

    public async Task<DteReserveResult> ReserveAsync(DteProviderContext ctx, string xml, int dteType, Guid emissionKey, CancellationToken ct)
    {
        var r = await CallAsync(ctx, "generaDTE", ct,
            ("rutEmisor", ctx.IssuerTaxId), ("tipoDTE", Int(dteType)), ("formato", "XML"), ("xml", xml), ("uuid", emissionKey.ToString()));
        if (IsOk(r)) return new DteReserveResult(true, long.Parse(Child(r, "folioDTE")!, CultureInfo.InvariantCulture), null, false);

        var message = Child(r, "descripcionOperacion") ?? "FACELE rechazó el documento sin detalle.";
        var used = UuidUsedRegex().Match(message);
        return used.Success
            ? new DteReserveResult(false, long.Parse(used.Groups[1].Value, CultureInfo.InvariantCulture), null, true)
            : new DteReserveResult(false, null, message, false);
    }

    public async Task<DteConfirmResult> ConfirmAsync(DteProviderContext ctx, int dteType, long folio, CancellationToken ct)
    {
        var r = await CallAsync(ctx, "obtieneDTE", ct,
            ("rutEmisor", ctx.IssuerTaxId), ("tipoDTE", Int(dteType)), ("folioDTE", Long(folio)), ("formato", "XML"),
            ("cedible", "false"), ("cantidad", "1"));
        return IsOk(r)
            ? new DteConfirmResult(true, Child(r, "XML"), null)
            : new DteConfirmResult(false, null, Child(r, "descripcionOperacion"));
    }

    public async Task ReleaseAsync(DteProviderContext ctx, int dteType, long folio, CancellationToken ct)
    {
        var r = await CallAsync(ctx, "controversia", ct,
            ("rutEmisor", ctx.IssuerTaxId), ("tipoDTE", Int(dteType)), ("folioDTE", Long(folio)), ("operacion", "libera"));
        EnsureOk(r, "liberar el folio");
    }

    public async Task<byte[]> GetPdfAsync(DteProviderContext ctx, int dteType, long folio, bool cedible, int copies, CancellationToken ct)
    {
        var r = await CallAsync(ctx, "obtieneDTE", ct,
            ("rutEmisor", ctx.IssuerTaxId), ("tipoDTE", Int(dteType)), ("folioDTE", Long(folio)), ("formato", "PDF"),
            ("cedible", cedible ? "true" : "false"), ("cantidad", Int(Math.Max(1, copies))));
        EnsureOk(r, "obtener el PDF");
        return DecodePdf(r);
    }

    public async Task TestAsync(DteProviderContext ctx, CancellationToken ct)
    {
        var r = await CallAsync(ctx, "sinConfirmar", ct, ("rutEmisor", ctx.IssuerTaxId));
        EnsureOk(r, "consultar documentos sin confirmar");
    }

    private Task<XElement> CallAsync(DteProviderContext ctx, string op, CancellationToken ct, params (string, string)[] p) =>
        _soap.CallAsync(ctx, FaceleSoapClient.EmissionService, op, p, ct);

    private static bool IsOk(XElement r) => Child(r, "estadoOperacion") == "1";

    /// <summary>Error de negocio en operaciones que no son la emisión: se informa como InvalidOperationException presentable.</summary>
    private static void EnsureOk(XElement r, string what)
    {
        if (!IsOk(r))
            throw new InvalidOperationException($"FACELE no pudo {what}: {Child(r, "descripcionOperacion") ?? "sin detalle"}");
    }

    /// <summary>Hijo por nombre local sin importar mayúsculas (obtieneDTE devuelve &lt;XML&gt;, Obtener &lt;xml&gt;).</summary>
    private static string? Child(XElement r, string name) =>
        r.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>El PDF de FACELE viene en base64 dos veces.</summary>
    private static byte[] DecodePdf(XElement r)
    {
        var outer = Child(r, "PDF") ?? throw new DteProviderUnavailableException("Respuesta de FACELE sin PDF.");
        var inner = Encoding.ASCII.GetString(Convert.FromBase64String(outer.Trim()));
        return Convert.FromBase64String(inner.Trim());
    }

    private static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Long(long v) => v.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"UUID \[[^\]]*\] ya utilizado para DTE: \[.*?Folio: (\d+)")]
    private static partial Regex UuidUsedRegex();
}
