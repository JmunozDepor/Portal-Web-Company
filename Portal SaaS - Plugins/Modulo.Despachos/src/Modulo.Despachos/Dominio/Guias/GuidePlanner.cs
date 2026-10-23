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
    TimeOnly DepartureTime);

public sealed record PlannedGuide(int Sequence, IReadOnlyList<Guid> ManifestDocumentIds, DteDocument Document);

public sealed record GuidePlan(IReadOnlyList<PlannedGuide> Guides, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>
/// Arma las guías de despacho (DTE 52) de un manifiesto, sin I/O:
/// - una guía por destino (RUT receptor + dirección normalizada + comuna), en el orden de aparición;
/// - detalle consolidado por artículo, sin precios (montos 0, la mercadería ya está facturada) [validar con el asesor];
/// - una referencia por factura amparada;
/// - división cuando el destino excede 60 líneas o 40 referencias (formato DTE); una factura de más de 60
///   artículos se reparte en trozos de 60 y cada trozo la referencia;
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
            if (commune is null || !RutChileno.IsValid(doc.CustomerTaxId) || string.IsNullOrWhiteSpace(doc.DestinationAddress)
                || lines.Lines.Count == 0)
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
            foreach (var chunk in Split(g.Docs))
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

    private sealed record ConsolidatedLine(string Key, string ItemCode, string Name, decimal Quantity, string? Unit, decimal? WeightKg);

    private sealed class Group(string key, string rut, ManifestDocument first, string commune, SapDocumentLines lines)
    {
        public string Key { get; } = key;
        public string Rut { get; } = rut;
        public ManifestDocument First { get; } = first;
        public string Commune { get; } = commune;
        public SapDocumentLines FirstLines { get; } = lines;
        public List<(ManifestDocument Doc, SapDocumentLines Lines)> Docs { get; } = new();
    }

    private static IEnumerable<List<ChunkPart>> Split(List<(ManifestDocument Doc, SapDocumentLines Lines)> docs)
    {
        var current = new List<ChunkPart>();
        var currentKeys = new HashSet<string>();
        foreach (var (doc, lines) in docs)
        {
            var own = Consolidate(lines.Lines);
            var newKeys = own.Count(l => !currentKeys.Contains(l.Key));
            if (current.Count > 0 && (currentKeys.Count + newKeys > MaxLines || current.Count + 1 > MaxReferences))
            {
                yield return current;
                current = new List<ChunkPart>();
                currentKeys = new HashSet<string>();
            }
            if (own.Count > MaxLines)
            {
                var pieces = own.Chunk(MaxLines).ToList();
                for (var i = 0; i < pieces.Count; i++)
                    yield return new List<ChunkPart> { new(doc, pieces[i], CountsPackages: i == 0) };
                continue;
            }
            current.Add(new ChunkPart(doc, own, CountsPackages: true));
            foreach (var l in own) currentKeys.Add(l.Key);
        }
        if (current.Count > 0) yield return current;
    }

    private static List<ConsolidatedLine> Consolidate(IEnumerable<SapDocumentLine> lines)
    {
        var result = new List<ConsolidatedLine>();
        foreach (var l in lines)
        {
            var key = string.IsNullOrWhiteSpace(l.ItemCode) ? "#" + l.ItemName.Trim().ToUpperInvariant() : l.ItemCode.Trim();
            var i = result.FindIndex(x => x.Key == key);
            if (i < 0) result.Add(new ConsolidatedLine(key, l.ItemCode.Trim(), l.ItemName, l.Quantity, l.Unit, l.WeightKg));
            else
            {
                var x = result[i];
                result[i] = x with
                {
                    Quantity = x.Quantity + l.Quantity,
                    Unit = x.Unit ?? l.Unit,
                    WeightKg = x.WeightKg is null || l.WeightKg is null ? null : x.WeightKg + l.WeightKg,
                };
            }
        }
        return result;
    }

    private static DteDocument BuildDocument(GuidePlanInput input, Group g, List<ChunkPart> chunk, out int missingWeight)
    {
        var m = input.Manifest;
        var merged = Consolidate(chunk.SelectMany(c => c.Lines.Select(l =>
            new SapDocumentLine(0, l.ItemCode, l.Name, l.Quantity, l.Unit, l.WeightKg))));
        missingWeight = merged.Count(l => l.WeightKg is null);
        decimal? weight = missingWeight == 0 ? merged.Sum(l => l.WeightKg!.Value) : null;
        var packages = chunk.Where(c => c.CountsPackages).Sum(c => c.Doc.Packages);

        var lines = merged.Select((l, i) => new DteLine(
            i + 1, string.IsNullOrEmpty(l.ItemCode) ? null : l.ItemCode, l.Name, null, l.Quantity, l.Unit, null, 0)).ToList();
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

    private static string NormalizeAddress(string address) => SpacesRegex().Replace(address.Trim(), " ").ToUpperInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
