using System.Text.RegularExpressions;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.Despachos.Dominio.Guias;

/// <summary>Entrada del planificador: manifiesto (con su snapshot Res. 154) y cada factura con sus líneas SAP.</summary>
public sealed record GuidePlanInput(
    Manifest Manifest,
    IReadOnlyList<(ManifestDocument Doc, SapDocumentLines Lines)> Documents,
    Func<long?, string?> CommuneName,
    int TransferIndicator,
    int? DispatchType,
    DateOnly IssueDate,
    TimeOnly DepartureTime,
    string LineGrouping = GuideLineGroupings.Sku,
    string ItemCodePlacement = GuideItemCodePlacements.Column,
    int MaxLines = GuidePlanner.MaxLines,
    bool Valued = false);

public sealed record PlannedGuide(int Sequence, IReadOnlyList<Guid> ManifestDocumentIds, DteDocument Document);

public sealed record GuidePlan(IReadOnlyList<PlannedGuide> Guides, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>
/// Arma las guías de despacho (DTE 52) de un manifiesto, sin I/O:
/// - una guía por destino (RUT receptor + dirección normalizada + comuna), en el orden de aparición;
/// - detalle consolidado por SKU o por modelo (LineGrouping); valorizado con los montos netos de las facturas
///   (Valued: precio = monto / cantidad; las líneas de facturas exentas 34 van exentas y aparte) o sin precios;
/// - una referencia por factura amparada;
/// - división cuando el destino excede el máximo de líneas (60 del formato DTE, o menos si la compañía lo baja
///   para que la plantilla PDF no se monte sobre el pie) o 40 referencias; una factura con más líneas que el
///   máximo se reparte en trozos y cada trozo la referencia;
/// - transporte desde el snapshot del manifiesto.
/// Errores = bloquean el despacho; advertencias = se emite igual.
/// </summary>
public static partial class GuidePlanner
{
    public const int MaxLines = 60;
    public const int MaxReferences = 40;

    public static GuidePlan Plan(GuidePlanInput input)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var m = input.Manifest;

        if (m.EstimatedArrivalDate is null)
            errors.Add("Ingrese la fecha de llegada estimada (obligatoria en la guía de despacho, Res. 154).");

        var groups = new List<Group>();
        foreach (var (doc, lines) in input.Documents)
        {
            var prefix = $"Folio {doc.Folio}";
            if (doc.DocumentKind != DocumentKinds.Invoice)
            {
                errors.Add($"{prefix}: esta etapa solo emite guías para facturas.");
                continue;
            }
            var commune = input.CommuneName(doc.DestinationCommuneId);
            if (commune is null) errors.Add($"{prefix}: la comuna de destino no está resuelta; corríjala en la pestaña Documentos.");
            if (!RutChileno.IsValid(doc.CustomerTaxId)) errors.Add($"{prefix}: el RUT del cliente «{doc.CustomerTaxId}» no es válido.");
            if (string.IsNullOrWhiteSpace(doc.DestinationAddress)) errors.Add($"{prefix}: falta la dirección de despacho en SAP.");
            if (lines.Lines.Count == 0) errors.Add($"{prefix}: la consulta SAP no devolvió líneas de la factura.");
            var noAmount = input.Valued && lines.Lines.Any(l => l.Amount is null);
            if (noAmount)
                errors.Add($"{prefix}: la consulta SAP de líneas no trae el monto (columna line_amount); no se puede valorizar la guía.");
            if (commune is null || !RutChileno.IsValid(doc.CustomerTaxId) || string.IsNullOrWhiteSpace(doc.DestinationAddress)
                || lines.Lines.Count == 0 || noAmount)
                continue;

            var rut = RutChileno.Normalize(doc.CustomerTaxId!);
            var key = $"{rut}|{NormalizeAddress(doc.DestinationAddress!)}|{doc.DestinationCommuneId}";
            var group = groups.FirstOrDefault(g => g.Key == key);
            if (group is null)
            {
                group = new Group(key, rut, doc, commune, lines);
                groups.Add(group);
            }
            group.Docs.Add((doc, lines));
        }

        var guides = new List<PlannedGuide>();
        var missingWeight = 0;
        foreach (var g in groups)
        {
            foreach (var chunk in Split(g.Docs, input.LineGrouping == GuideLineGroupings.Model, Math.Clamp(input.MaxLines, 1, MaxLines), input.Valued))
            {
                var document = BuildDocument(input, g, chunk, out var noWeight);
                missingWeight += noWeight;
                guides.Add(new PlannedGuide(guides.Count + 1, chunk.Select(c => c.Doc.Id).Distinct().ToList(), document));
            }
        }
        if (missingWeight > 0)
            warnings.Add($"Peso no disponible en SAP para {missingWeight} artículo(s): la guía sale sin peso.");

        return new GuidePlan(errors.Count > 0 ? Array.Empty<PlannedGuide>() : guides, errors, warnings);
    }

    /// <summary>Trozo de guía: documento + las líneas consolidadas suyas que van en ESTA guía + si aporta los bultos.</summary>
    private sealed record ChunkPart(ManifestDocument Doc, IReadOnlyList<ConsolidatedLine> Lines, bool CountsPackages);

    private sealed record ConsolidatedLine(string Key, string ItemCode, string Name, decimal Quantity, string? Unit, decimal? WeightKg,
        decimal Amount, bool Exempt);

    private sealed class Group(string key, string rut, ManifestDocument first, string commune, SapDocumentLines lines)
    {
        public string Key { get; } = key;
        public string Rut { get; } = rut;
        public ManifestDocument First { get; } = first;
        public string Commune { get; } = commune;
        public SapDocumentLines FirstLines { get; } = lines;
        public List<(ManifestDocument Doc, SapDocumentLines Lines)> Docs { get; } = new();
    }

    private static IEnumerable<List<ChunkPart>> Split(List<(ManifestDocument Doc, SapDocumentLines Lines)> docs, bool byModel, int maxLines, bool valued)
    {
        var current = new List<ChunkPart>();
        var currentKeys = new HashSet<string>();
        foreach (var (doc, lines) in docs)
        {
            // Valorizada: las líneas de una factura exenta (34) van exentas y no se juntan con las afectas.
            var own = Consolidate(lines.Lines, byModel, exempt: valued && doc.SiiDocumentType == 34);
            var newKeys = own.Count(l => !currentKeys.Contains(l.Key));
            if (current.Count > 0 && (currentKeys.Count + newKeys > maxLines || current.Count + 1 > MaxReferences))
            {
                yield return current;
                current = new List<ChunkPart>();
                currentKeys = new HashSet<string>();
            }
            if (own.Count > maxLines)
            {
                var pieces = own.Chunk(maxLines).ToList();
                for (var i = 0; i < pieces.Count; i++)
                    yield return new List<ChunkPart> { new(doc, pieces[i], CountsPackages: i == 0) };
                continue;
            }
            current.Add(new ChunkPart(doc, own, CountsPackages: true));
            foreach (var l in own) currentKeys.Add(l.Key);
        }
        if (current.Count > 0) yield return current;
    }

    /// <summary>
    /// Suma las líneas por SKU o, con <paramref name="byModel"/>, por modelo (las tallas de un modelo quedan en una
    /// línea con el código y nombre del modelo; sin nombre de modelo se usa el del primer artículo). Una línea sin
    /// modelo queda por SKU. El nombre de una línea que junta varias es el mayor (orden ordinal): estable sin
    /// importar el orden de las facturas ni de las tallas.
    /// </summary>
    private static List<ConsolidatedLine> Consolidate(IEnumerable<SapDocumentLine> lines, bool byModel, bool exempt)
    {
        var result = new List<ConsolidatedLine>();
        foreach (var l in lines)
        {
            var model = byModel && !string.IsNullOrWhiteSpace(l.ModelCode) ? l.ModelCode.Trim() : null;
            var code = model ?? l.ItemCode.Trim();
            var key = (exempt ? "E|" : "") + (model is not null ? "M:" + model
                : string.IsNullOrWhiteSpace(l.ItemCode) ? "#" + l.ItemName.Trim().ToUpperInvariant() : l.ItemCode.Trim());
            var name = model is not null && !string.IsNullOrWhiteSpace(l.ModelName) ? l.ModelName.Trim() : l.ItemName;
            var i = result.FindIndex(x => x.Key == key);
            if (i < 0) result.Add(new ConsolidatedLine(key, code, name, l.Quantity, l.Unit, l.WeightKg, l.Amount ?? 0, exempt));
            else
            {
                var x = result[i];
                result[i] = x with
                {
                    Name = MaxName(x.Name, name),
                    Quantity = x.Quantity + l.Quantity,
                    Unit = x.Unit ?? l.Unit,
                    WeightKg = x.WeightKg is null || l.WeightKg is null ? null : x.WeightKg + l.WeightKg,
                    Amount = x.Amount + (l.Amount ?? 0),
                };
            }
        }
        return result;
    }

    private static DteDocument BuildDocument(GuidePlanInput input, Group g, List<ChunkPart> chunk, out int missingWeight)
    {
        var m = input.Manifest;
        // Las líneas ya vienen resumidas (por SKU o modelo): se juntan las de las facturas del trozo por su llave.
        var merged = chunk.SelectMany(c => c.Lines).GroupBy(l => l.Key).Select(grp => grp.Aggregate((x, l) => x with
        {
            Name = MaxName(x.Name, l.Name),
            Quantity = x.Quantity + l.Quantity,
            Unit = x.Unit ?? l.Unit,
            WeightKg = x.WeightKg is null || l.WeightKg is null ? null : x.WeightKg + l.WeightKg,
            Amount = x.Amount + l.Amount,
        })).ToList();
        missingWeight = merged.Count(l => l.WeightKg is null);
        decimal? weight = missingWeight == 0 ? merged.Sum(l => l.WeightKg!.Value) : null;
        var packages = chunk.Where(c => c.CountsPackages).Sum(c => c.Doc.Packages);

        // Código al inicio del detalle: la columna Código de algunas plantillas PDF es angosta y lo superpone.
        var codeInName = input.ItemCodePlacement == GuideItemCodePlacements.Name;
        var lines = merged.Select((l, i) =>
        {
            // Valorizada: precio = monto / cantidad (6 decimales, lo que admite el SII) y monto = cantidad x precio
            // redondeado, para que cuadre con la validación del SII.
            decimal? price = input.Valued && l.Quantity > 0 ? Math.Round(l.Amount / l.Quantity, 6, MidpointRounding.AwayFromZero) : null;
            var amount = price is { } p ? Math.Round(l.Quantity * p, 0, MidpointRounding.AwayFromZero) : 0;
            return new DteLine(
                i + 1,
                codeInName || string.IsNullOrEmpty(l.ItemCode) ? null : l.ItemCode,
                codeInName && !string.IsNullOrEmpty(l.ItemCode) ? $"{l.ItemCode} {l.Name}" : l.Name,
                null, l.Quantity, l.Unit, price, amount, Exempt: input.Valued && l.Exempt);
        }).ToList();
        var refs = chunk.Select(c => c.Doc).DistinctBy(d => d.Id).Select((d, i) => new DteReference(
            i + 1, d.SiiDocumentType.ToString(System.Globalization.CultureInfo.InvariantCulture), d.Folio, d.DocumentDate, null,
            $"Traslado de mercadería facturada (manifiesto {m.Number})")).ToList();

        var address = g.First.DestinationAddress!.Trim();
        var city = g.FirstLines.DestinationCity;
        return new DteDocument(
            new DteIdDoc(52, input.IssueDate, input.TransferIndicator, input.DispatchType),
            new DteIssuerOverride(m.OriginAddress, m.OriginCommune, null),
            new DteReceiver(g.Rut, g.First.CustomerName, g.FirstLines.CustomerActivity, address, g.Commune, city),
            new DteTransport(
                m.Plate, m.TrailerPlate, m.CarrierTaxId,
                m.DriverTaxId is null && m.DriverName is null ? null : new DteDriver(m.DriverTaxId ?? "", m.DriverName ?? ""),
                address, g.Commune, city,
                new DteCustoms(weight, packages > 0 ? packages : null, null),
                input.IssueDate, input.DepartureTime, m.EstimatedArrivalDate),
            null, lines, refs, Array.Empty<DteExtraField>());
    }

    private static string MaxName(string a, string b) => string.CompareOrdinal(a, b) >= 0 ? a : b;

    private static string NormalizeAddress(string address) => SpacesRegex().Replace(address.Trim(), " ").ToUpperInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
