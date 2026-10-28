using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PortalSaas.Abstractions.Modelos.Dte;

namespace PortalSaas.Core.DocumentosElectronicos.Proveedores.Facele;

/// <summary>
/// Recepción de documentos de FACELE (servicio DTEService, WSDL https://caoba.docele.cl/cl-dol/DTEService?wsdl):
/// consultar (operacion=RECEPCION), consultarReferencia y aprobarDTERecibidos. Las respuestas traen
/// &lt;respuesta&gt;&lt;estado/&gt;&lt;descripcion/&gt;&lt;/respuesta&gt;; se toma estado = 1 como OK, igual que
/// estadoOperacion en la emisión (a confirmar en caoba). XML/PDF del recibido y observaciones quedan sin
/// capacidad hasta verificarlos en caoba (spec Facturas de Proveedor §16).
/// </summary>
public sealed partial class FaceleReceptionProvider : IDteReceptionProvider
{
    private const string Operation = "RECEPCION";
    private static readonly TimeZoneInfo Chile = ResolveChile();

    private static readonly Dictionary<string, string> Actions = new()
    {
        [DteResponseActions.AcceptContent] = "Acepta_Contenido_Documento",
        [DteResponseActions.ClaimContent] = "Reclamo_Contenido_Documento",
        [DteResponseActions.ClaimPartialGoods] = "Reclamo_Falta_Parcial_Mercaderias",
        [DteResponseActions.ClaimTotalGoods] = "Reclamo_Falta_Total_Mercaderias",
        [DteResponseActions.GoodsReceipt] = "Otorga_Recibo_Mercaderias",
    };

    private readonly FaceleSoapClient _soap;

    public FaceleReceptionProvider(FaceleSoapClient soap) => _soap = soap;

    public string Code => "FACELE";

    public DteReceptionCapabilities Capabilities { get; } = new(
        new[] { 33, 34, 43 }, DteResponseActions.All, SupportsResponseNote: false, SupportsDocumentDownload: false);

    /// <summary>Registros por página de consultar (limit). Interno para poder probar la paginación.</summary>
    internal int PageSize { get; set; } = 100;

    public async Task<IReadOnlyList<DteReceivedRecord>> ListReceivedAsync(DteReceptionContext ctx, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var result = new List<DteReceivedRecord>();
        for (var offset = 0; ; offset += PageSize)
        {
            var r = await CallAsync(ctx, "consultar", ct,
                E("rutAbonado", ctx.ReceiverTaxId), E("userMail", ctx.UserMail),
                E("fechaEmision", Date(from)), E("fechaEmisionHasta", Date(to)),
                E("operacion", Operation), E("offset", Int(offset)), E("limit", Int(PageSize)));
            EnsureOk(r, "consultar los documentos recibidos");
            var page = r.Elements().Where(e => e.Name.LocalName == "registros").Select(ToRecord).ToList();
            result.AddRange(page);
            if (page.Count < PageSize) return result;
        }
    }

    public async Task<IReadOnlyList<DteReceivedReference>> GetReferencesAsync(
        DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, CancellationToken ct)
    {
        var r = await CallAsync(ctx, "consultarReferencia", ct,
            E("rutAbonado", ctx.ReceiverTaxId), E("userMail", ctx.UserMail), E("rutEmisor", issuerTaxId),
            E("tipoDTE", Int(dteType)), E("folio", Long(folio)), E("operacion", Operation));
        EnsureOk(r, "consultar las referencias del documento");
        return r.Elements().Where(e => e.Name.LocalName == "referenciado")
            .Select(x => new DteReceivedReference(
                Child(x, "tipo") ?? "",
                Child(x, "folio") ?? "",
                ParseDate(Child(x, "fechaEmision")),
                int.TryParse(Child(x, "codigoReferencia"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? code : null,
                Child(x, "razonReferencia")))
            .ToList();
    }

    public Task<string?> GetXmlAsync(DteReceptionContext ctx, DteReceivedRecord record, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    public Task<byte[]?> GetPdfAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, string? url, CancellationToken ct) =>
        Task.FromResult<byte[]?>(null);

    public async Task<DteRespondProviderResult> RespondAsync(
        DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, string action, CancellationToken ct)
    {
        if (!Actions.TryGetValue(action, out var faceleAction))
            throw new ArgumentException($"Acción de respuesta desconocida: {action}.", nameof(action));
        var r = await CallAsync(ctx, "aprobarDTERecibidos", ct,
            E("rutAbonado", ctx.ReceiverTaxId), E("userMail", ctx.UserMail),
            new XElement("aprobacion",
                E("rutEmisor", issuerTaxId), E("tipoDTE", Int(dteType)), E("folio", Long(folio)), E("accion", faceleAction)));
        if (IsOk(r)) return new DteRespondProviderResult(true, false, null);
        var message = Description(r) ?? "FACELE rechazó la respuesta sin detalle.";
        return new DteRespondProviderResult(false, AlreadyRespondedRegex().IsMatch(message), message);
    }

    public Task SetNoteAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, string note, CancellationToken ct) =>
        throw new NotSupportedException("FACELE: observaciones en documentos recibidos no habilitadas (pendiente de verificar en caoba).");

    public async Task TestAsync(DteReceptionContext ctx, CancellationToken ct)
    {
        var r = await CallAsync(ctx, "consultarResumen", ct,
            E("rutAbonado", ctx.ReceiverTaxId), E("operacion", Operation), E("offset", "0"));
        if (!IsOk(r))
            throw new DteProviderUnavailableException($"FACELE rechazó la consulta de recibidos: {Description(r) ?? "sin detalle"}.");
    }

    private Task<XElement> CallAsync(DteReceptionContext ctx, string op, CancellationToken ct, params XElement[] body) =>
        _soap.CallAsync(ctx.BaseUrl, ctx.Username, ctx.Password, FaceleSoapClient.ReceptionService, op, body, ct);

    private static DteReceivedRecord ToRecord(XElement x)
    {
        var taxes = x.Elements().Where(e => e.Name.LocalName == "impuestoAdicional")
            .Select(t => new { code = Child(t, "codigo"), rate = Child(t, "tasa"), amount = Child(t, "monto") })
            .ToList();
        return new DteReceivedRecord(
            IssuerTaxId: Child(x, "rutEmisor") ?? "",
            IssuerName: Child(x, "razonSocialEmisor") ?? "",
            DteType: int.Parse(Child(x, "tipoDTE") ?? "0", CultureInfo.InvariantCulture),
            Folio: long.Parse(Child(x, "folioDTE") ?? "0", CultureInfo.InvariantCulture),
            IssueDate: ParseDate(Child(x, "fechaEmision")) ?? DateOnly.MinValue,
            SiiReceivedAt: ParseChileDateTime(Child(x, "fechaRegistroSII")),
            Net: Amount(x, "montoNeto"),
            Exempt: Amount(x, "montoExento"),
            Vat: Amount(x, "montoIVA"),
            Total: Amount(x, "montoTotal"),
            OtherTaxesJson: taxes.Count == 0 ? null : JsonSerializer.Serialize(taxes),
            SiiStatus: Child(x, "estadoSII"),
            CommercialStatus: Child(x, "estadoLey19983"),
            ClaimStatus: Child(x, "estadoLey20956"),
            IsAssigned: Child(x, "cedido") == "true",
            IsVoided: Child(x, "anulado") == "true",
            Url: Child(x, "url"));
    }

    /// <summary>
    /// xsd:dateTime de FACELE. Con zona horaria se respeta; sin ella se interpreta como hora de Chile. Siempre se
    /// devuelve en UTC (Npgsql solo escribe timestamptz con offset 0).
    /// </summary>
    internal static DateTimeOffset? ParseChileDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var hasZone = value.EndsWith('Z') || OffsetRegex().IsMatch(value);
        if (hasZone)
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal).ToUniversalTime();
        var local = DateTime.SpecifyKind(DateTime.Parse(value, CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Chile), TimeSpan.Zero);
    }

    private static TimeZoneInfo ResolveChile()
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value?.Length >= 10 ? value[..10] : value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    private static long Amount(XElement x, string name) =>
        long.TryParse(Child(x, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static XElement E(string name, string value) => new(name, value);

    private static bool IsOk(XElement r) =>
        r.Elements().FirstOrDefault(e => e.Name.LocalName == "respuesta") is { } resp && Child(resp, "estado") == "1";

    private static string? Description(XElement r) =>
        r.Elements().FirstOrDefault(e => e.Name.LocalName == "respuesta") is { } resp ? Child(resp, "descripcion") : null;

    private static void EnsureOk(XElement r, string what)
    {
        if (!IsOk(r))
            throw new DteProviderUnavailableException($"FACELE no pudo {what}: {Description(r) ?? "sin detalle"}");
    }

    private static string? Child(XElement r, string name) =>
        r.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } v ? v : null;

    private static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Long(long v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\bya\b.*(aprob|reclam|acept)", RegexOptions.IgnoreCase)]
    private static partial Regex AlreadyRespondedRegex();

    [GeneratedRegex(@"[+-]\d{2}:\d{2}$")]
    private static partial Regex OffsetRegex();
}
