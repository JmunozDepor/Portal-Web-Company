using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Modulo.Despachos.Data;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Servicios;

/// <summary>Fila del detalle del PDF (un documento).</summary>
public sealed record ManifestPdfRow(string Kind, long Folio, string Customer, string? Po, string? Address, decimal Units, int Packages, decimal Amount);

/// <summary>Grupo por destino (comuna) con sus subtotales.</summary>
public sealed record ManifestPdfGroup(string Title, IReadOnlyList<ManifestPdfRow> Rows)
{
    public int Documents => Rows.Count;
    public decimal Units => Rows.Sum(r => r.Units);
    public int Packages => Rows.Sum(r => r.Packages);
    public decimal Amount => Rows.Sum(r => r.Amount);
}

/// <summary>Datos que se imprimen: detalle agrupado por destino y totales generales.</summary>
public sealed record ManifestPdfModel(int Number, IReadOnlyList<ManifestPdfGroup> Groups)
{
    public int TotalDocuments => Groups.Sum(g => g.Documents);
    public decimal TotalUnits => Groups.Sum(g => g.Units);
    public int TotalPackages => Groups.Sum(g => g.Packages);
    public decimal TotalAmount => Groups.Sum(g => g.Amount);
}

public class ManifestPdfService : IManifestPdfService
{
    private readonly DespachosDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly IManifestService _manifests;
    private readonly TimeProvider _time;
    private readonly IHttpContextAccessor? _http;

    public ManifestPdfService(
        DespachosDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user,
        IManifestService manifests, TimeProvider time, IHttpContextAccessor? http = null)
    {
        _http = http;
        _db = db;
        _company = company;
        _user = user;
        _manifests = manifests;
        _time = time;
    }

    public async Task<byte[]> RenderAsync(Guid manifestId, bool isCopy)
    {
        var m = await _manifests.GetAsync(manifestId)
            ?? throw new InvalidOperationException("El manifiesto no existe.");
        // El borrador aún no es un documento oficial y el anulado no se imprime: así la primera impresión oficial
        // (tras despachar) nunca sale marcada como copia.
        if (m.Status == ManifestStatuses.Draft || m.Status == ManifestStatuses.Annulled)
            throw new InvalidOperationException("Solo se puede imprimir un manifiesto despachado (no un borrador ni uno anulado).");

        var communeIds = m.Documents.Where(d => d.DestinationCommuneId != null).Select(d => d.DestinationCommuneId!.Value).Distinct().ToList();
        var communes = await _db.Communes.Where(c => communeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
        var carrier = await _db.Carriers.FirstOrDefaultAsync(c => c.Id == m.CarrierId);
        var origin = await _db.Origins.FirstOrDefaultAsync(o => o.Id == m.OriginId);
        var shift = m.ShiftId == null ? null : await _db.Shifts.FirstOrDefaultAsync(s => s.Id == m.ShiftId);
        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.Id == m.DriverId);
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == m.VehicleId);
        var trailer = m.TrailerId == null ? null : await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == m.TrailerId);

        // En borrador aún no hay snapshot: se usa el maestro.
        var header = new Header(
            CompanyName(), origin?.Name ?? "", m.OriginAddress ?? origin?.Address, m.OriginCommune,
            shift?.Name, carrier?.Name ?? "", m.CarrierTaxId ?? carrier?.TaxId,
            m.DriverName ?? driver?.FullName ?? "", m.DriverTaxId ?? driver?.TaxId,
            m.Plate ?? vehicle?.Plate, m.TrailerPlate ?? trailer?.Plate);

        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var copyMark = isCopy
            ? $"COPIA - {ChileClock.ToLocal(nowUtc):dd-MM-yyyy HH:mm} - {_user.Username}"
            : null;

        var bytes = Build(m, BuildModel(m, communes), header, copyMark);

        _db.ManifestEvents.Add(new ManifestEvent
        {
            CompanyId = m.CompanyId, ManifestId = m.Id,
            EventType = isCopy ? ManifestEventTypes.Reprinted : ManifestEventTypes.Printed,
            UserId = _user.UserId, OccurredAt = nowUtc,
        });
        await _db.SaveChangesAsync();
        return bytes;
    }

    /// <summary>Nombre de la compañía de la sesión (claim CompanyName que fija el Host al elegir compañía); si no está, el código.</summary>
    private string CompanyName()
    {
        var name = _http?.HttpContext?.User.FindFirst("CompanyName")?.Value;
        return string.IsNullOrWhiteSpace(name) ? _company.Code : name.Trim();
    }

    /// <summary>Agrupa por comuna (o "Comuna no resuelta: texto"), grupos ordenados por nombre y filas por folio.</summary>
    internal static ManifestPdfModel BuildModel(Manifest m, IReadOnlyDictionary<long, string> communes)
    {
        static string Unresolved(ManifestDocument d) =>
            string.IsNullOrWhiteSpace(d.DestinationCommuneName)
                ? "Comuna no resuelta"
                : $"Comuna no resuelta: {d.DestinationCommuneName.Trim()}";

        string Title(ManifestDocument d) =>
            d.DestinationCommuneId != null && communes.TryGetValue(d.DestinationCommuneId.Value, out var name)
                ? name
                : Unresolved(d);

        var groups = m.Documents
            .GroupBy(Title, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new ManifestPdfGroup(g.Key, g
                .OrderBy(d => d.Folio)
                .Select(d => new ManifestPdfRow(
                    d.DocumentKind == DocumentKinds.TransferGuide ? "Guía" : "Factura",
                    d.Folio, d.CustomerName, d.CustomerPo, d.DestinationAddress, d.Units, d.Packages, d.TotalAmount))
                .ToList()))
            .ToList();
        return new ManifestPdfModel(m.Number, groups);
    }

    private sealed record Header(
        string CompanyName, string Origin, string? OriginAddress, string? OriginCommune, string? Shift,
        string Carrier, string? CarrierTaxId, string Driver, string? DriverTaxId, string? Plate, string? TrailerPlate);

    private static byte[] Build(Manifest m, ManifestPdfModel model, Header h, string? copyMark)
    {
        var ci = CultureInfo.GetCultureInfo("es-CL");
        var doc = new Document();
        doc.Info.Title = $"Manifiesto {m.Number}";
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 9;

        var s = doc.AddSection();
        s.PageSetup.PageFormat = PageFormat.Letter;
        s.PageSetup.LeftMargin = s.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        s.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
        s.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);

        if (copyMark != null)
        {
            var p = s.AddParagraph(copyMark);
            p.Format.Font.Size = 11;
            p.Format.Font.Bold = true;
            p.Format.Font.Color = Colors.Red;
            p.Format.Alignment = ParagraphAlignment.Right;
        }

        // Cabecera: título a la izquierda, código de barras a la derecha.
        // El código de barras va en su propia columna, con el número centrado bajo las barras.
        var top = s.AddTable();
        top.AddColumn(Unit.FromCentimeter(9.5));
        top.AddColumn(Unit.FromCentimeter(5.5));
        top.AddColumn(Unit.FromCentimeter(3));
        var topRow = top.AddRow();
        var title = topRow.Cells[0].AddParagraph($"MANIFIESTO DE DESPACHO N° {m.Number}");
        title.Format.Font.Size = 15;
        title.Format.Font.Bold = true;
        var company = topRow.Cells[0].AddParagraph(h.CompanyName);
        company.Format.Font.Size = 11;
        company.Format.Font.Bold = true;
        company.Format.SpaceBefore = Unit.FromPoint(2);
        topRow.Cells[0].AddParagraph("Documento interno");
        var png = BarcodeRenderer.Code128Png(m.Number.ToString(CultureInfo.InvariantCulture));
        var barcode = topRow.Cells[1].AddParagraph();
        barcode.Format.Alignment = ParagraphAlignment.Center;
        barcode.AddImage("base64:" + Convert.ToBase64String(png)).Width = Unit.FromCentimeter(5);
        var number = topRow.Cells[1].AddParagraph(m.Number.ToString(CultureInfo.InvariantCulture));
        number.Format.Alignment = ParagraphAlignment.Center;
        number.Format.Font.Size = 8;
        s.AddParagraph().Format.SpaceAfter = Unit.FromPoint(4);

        string Local(DateTime? utc) => utc == null ? "-" : ChileClock.ToLocal(utc.Value).ToString("dd-MM-yyyy HH:mm", ci);
        static string Rut(string? r) => string.IsNullOrWhiteSpace(r) ? "" : $" ({r})";
        var origin = string.Join(", ", new[] { h.Origin, h.OriginAddress, h.OriginCommune }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var eta = m.EstimatedArrivalDate == null ? "-"
            : m.EstimatedArrivalDate.Value.ToString("dd-MM-yyyy", ci)
              + (m.EstimatedArrivalTime == null ? "" : " " + m.EstimatedArrivalTime.Value.ToString("HH:mm", ci));
        var info = new (string, string)[]
        {
            ("Origen", origin),
            ("Fecha / turno", m.DispatchDate.ToString("dd-MM-yyyy", ci) + (string.IsNullOrWhiteSpace(h.Shift) ? "" : $" / {h.Shift}")),
            ("Transportista", h.Carrier + Rut(h.CarrierTaxId)),
            ("Chofer", h.Driver + Rut(h.DriverTaxId)),
            ("Patente / carro", (h.Plate ?? "-") + (string.IsNullOrWhiteSpace(h.TrailerPlate) ? "" : $" / {h.TrailerPlate}")),
            ("Salida", Local(m.DepartedAt)),
            ("Llegada estimada", eta),
        };
        var t = s.AddTable();
        t.AddColumn(Unit.FromCentimeter(3.5));
        t.AddColumn(Unit.FromCentimeter(14.5));
        foreach (var (k, v) in info)
        {
            var r = t.AddRow();
            r.Cells[0].AddParagraph(k).Format.Font.Bold = true;
            r.Cells[1].AddParagraph(v);
        }
        if (!string.IsNullOrWhiteSpace(m.Remarks))
        {
            var r = t.AddRow();
            r.Cells[0].AddParagraph("Observaciones").Format.Font.Bold = true;
            r.Cells[1].AddParagraph(m.Remarks);
        }
        s.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);

        // Detalle agrupado por destino.
        var d = s.AddTable();
        d.Borders.Width = 0.4;
        d.Borders.Color = Colors.Gray;
        foreach (var w in new[] { 1.6, 1.9, 6.0, 2.5, 1.8, 1.5, 2.7 }) d.AddColumn(Unit.FromCentimeter(w));
        var head = d.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = Colors.LightGray;
        head.Format.Font.Bold = true;
        var labels = new[] { "Tipo", "Folio", "Cliente", "OC", "Unidades", "Bultos", "Monto" };
        for (var i = 0; i < labels.Length; i++) head.Cells[i].AddParagraph(labels[i]);

        foreach (var g in model.Groups)
        {
            var gr = d.AddRow();
            gr.Shading.Color = Colors.WhiteSmoke;
            gr.Format.Font.Bold = true;
            gr.Cells[0].MergeRight = 6;
            gr.Cells[0].AddParagraph($"Destino: {g.Title}");
            foreach (var row in g.Rows)
            {
                var rr = d.AddRow();
                rr.Cells[0].AddParagraph(row.Kind);
                rr.Cells[1].AddParagraph(row.Folio.ToString(CultureInfo.InvariantCulture));
                rr.Cells[2].AddParagraph(row.Customer);
                rr.Cells[3].AddParagraph(row.Po ?? "");
                rr.Cells[4].AddParagraph(row.Units.ToString("N0", ci)).Format.Alignment = ParagraphAlignment.Right;
                rr.Cells[5].AddParagraph(row.Packages.ToString(ci)).Format.Alignment = ParagraphAlignment.Right;
                rr.Cells[6].AddParagraph("$" + row.Amount.ToString("N0", ci)).Format.Alignment = ParagraphAlignment.Right;
            }
            var sub = d.AddRow();
            sub.Format.Font.Italic = true;
            sub.Cells[0].MergeRight = 3;
            sub.Cells[0].AddParagraph($"Subtotal {g.Title}: {g.Documents} documento(s)").Format.Alignment = ParagraphAlignment.Right;
            sub.Cells[4].AddParagraph(g.Units.ToString("N0", ci)).Format.Alignment = ParagraphAlignment.Right;
            sub.Cells[5].AddParagraph(g.Packages.ToString(ci)).Format.Alignment = ParagraphAlignment.Right;
            sub.Cells[6].AddParagraph("$" + g.Amount.ToString("N0", ci)).Format.Alignment = ParagraphAlignment.Right;
        }
        var tot = d.AddRow();
        tot.Format.Font.Bold = true;
        tot.Shading.Color = Colors.LightGray;
        tot.Cells[0].MergeRight = 3;
        tot.Cells[0].AddParagraph($"TOTAL GENERAL: {model.TotalDocuments} documento(s)").Format.Alignment = ParagraphAlignment.Right;
        tot.Cells[4].AddParagraph(model.TotalUnits.ToString("N0", ci)).Format.Alignment = ParagraphAlignment.Right;
        tot.Cells[5].AddParagraph(model.TotalPackages.ToString(ci)).Format.Alignment = ParagraphAlignment.Right;
        tot.Cells[6].AddParagraph("$" + model.TotalAmount.ToString("N0", ci)).Format.Alignment = ParagraphAlignment.Right;

        // Firmas.
        s.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(2);
        var sig = s.AddTable();
        for (var i = 0; i < 3; i++) sig.AddColumn(Unit.FromCentimeter(6));
        var line = sig.AddRow();
        var signLabels = new[] { "Despacho", "Chofer", "Recepción del retorno" };
        for (var i = 0; i < signLabels.Length; i++)
        {
            var c = line.Cells[i];
            c.Borders.Top.Width = 0.5;
            c.Format.Alignment = ParagraphAlignment.Center;
            c.AddParagraph(signLabels[i]);
        }

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var ms = new MemoryStream();
        renderer.PdfDocument.Save(ms, false);
        return ms.ToArray();
    }
}
