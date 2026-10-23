using System.Globalization;
using Microsoft.AspNetCore.Http;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Servicios;

/// <summary>Hoja de agrupaciones (una página por agrupación) que despacho escanea en el manifiesto. Solo cantidades.</summary>
public interface IDispatchGroupPdfService
{
    /// <summary>PDF con una página por agrupación, en el orden recibido. Lanza si ninguna existe.</summary>
    Task<byte[]> RenderAsync(IReadOnlyList<long> groupIds);
}

public class DispatchGroupPdfService : IDispatchGroupPdfService
{
    private readonly IDispatchGroupService _groups;
    private readonly ICurrentCompanyAccessor _company;
    private readonly IHttpContextAccessor? _http;

    public DispatchGroupPdfService(IDispatchGroupService groups, ICurrentCompanyAccessor company, IHttpContextAccessor? http = null)
    {
        _groups = groups;
        _company = company;
        _http = http;
    }

    public async Task<byte[]> RenderAsync(IReadOnlyList<long> groupIds)
    {
        var groups = new List<DispatchGroup>();
        foreach (var id in groupIds.Distinct())
            if (await _groups.GetAsync(id) is { } g) groups.Add(g);
        if (groups.Count == 0) throw new InvalidOperationException("No hay agrupaciones para imprimir.");
        return Build(groups, CompanyName());
    }

    /// <summary>Nombre de la compañía de la sesión (claim CompanyName); si no está, el código.</summary>
    private string CompanyName()
    {
        var name = _http?.HttpContext?.User.FindFirst("CompanyName")?.Value;
        return string.IsNullOrWhiteSpace(name) ? _company.Code : name.Trim();
    }

    private static byte[] Build(IReadOnlyList<DispatchGroup> groups, string companyName)
    {
        var ci = CultureInfo.GetCultureInfo("es-CL");
        var doc = new Document();
        doc.Info.Title = groups.Count == 1 ? DispatchGroupCode.Format(groups[0].Number) : "Agrupaciones";
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 10;

        foreach (var g in groups)
        {
            var code = DispatchGroupCode.Format(g.Number);
            var s = doc.AddSection();
            s.PageSetup.PageFormat = PageFormat.Letter;
            s.PageSetup.LeftMargin = s.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
            s.PageSetup.TopMargin = s.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);

            // Cabecera: datos a la izquierda, código de barras (lo que escanea despacho) a la derecha.
            var top = s.AddTable();
            top.AddColumn(Unit.FromCentimeter(9.5));
            top.AddColumn(Unit.FromCentimeter(8.5));
            var row = top.AddRow();
            var title = row.Cells[0].AddParagraph($"AGRUPACIÓN {code}");
            title.Format.Font.Size = 16;
            title.Format.Font.Bold = true;
            var company = row.Cells[0].AddParagraph(companyName);
            company.Format.Font.Bold = true;
            company.Format.SpaceBefore = Unit.FromPoint(2);
            row.Cells[0].AddParagraph($"Sucursal {g.WarehouseCode} – {g.BranchName}");
            row.Cells[0].AddParagraph($"Despacho {g.DispatchDate.ToString("dd-MM-yyyy", ci)}   Grupo {g.Sequence}");
            var barcode = row.Cells[1].AddParagraph();
            barcode.Format.Alignment = ParagraphAlignment.Center;
            barcode.AddImage("base64:" + Convert.ToBase64String(BarcodeRenderer.Code128Png(code, 480, 110))).Width = Unit.FromCentimeter(8);
            var codeText = row.Cells[1].AddParagraph(code);
            codeText.Format.Alignment = ParagraphAlignment.Center;
            codeText.Format.Font.Size = 14;
            codeText.Format.Font.Bold = true;
            s.AddParagraph().Format.SpaceAfter = Unit.FromPoint(8);

            var t = s.AddTable();
            t.Borders.Width = 0.4;
            t.Borders.Color = Colors.Gray;
            foreach (var w in new[] { 8.0, 5.0, 5.0 }) t.AddColumn(Unit.FromCentimeter(w));
            var head = t.AddRow();
            head.HeadingFormat = true;
            head.Shading.Color = Colors.LightGray;
            head.Format.Font.Bold = true;
            head.Cells[0].AddParagraph("LPN");
            head.Cells[1].AddParagraph("Transferencia");
            head.Cells[2].AddParagraph("Unidades").Format.Alignment = ParagraphAlignment.Right;
            foreach (var line in g.Transfers.OrderBy(x => x.SapDocNum))
            {
                var r = t.AddRow();
                r.Cells[0].AddParagraph(string.IsNullOrWhiteSpace(line.Lpn) ? "—" : line.Lpn);
                r.Cells[1].AddParagraph(line.SapDocNum.ToString(CultureInfo.InvariantCulture));
                r.Cells[2].AddParagraph(line.Units.ToString("N0", ci)).Format.Alignment = ParagraphAlignment.Right;
            }
            var tot = t.AddRow();
            tot.Format.Font.Bold = true;
            tot.Shading.Color = Colors.LightGray;
            tot.Cells[0].MergeRight = 1;
            tot.Cells[0].AddParagraph($"Total: {g.Transfers.Count} LPN");
            tot.Cells[2].AddParagraph(g.Transfers.Sum(x => x.Units).ToString("N0", ci) + " unidades").Format.Alignment = ParagraphAlignment.Right;
        }

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var ms = new MemoryStream();
        renderer.PdfDocument.Save(ms, false);
        return ms.ToArray();
    }
}
