using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Models;
using Modulo.Despachos.Pages;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Servicios;

public sealed class DocumentReportService : IDocumentReportService
{
    private readonly DespachosDbContext _db;
    private readonly ICurrentCompanyAccessor _company;

    public DocumentReportService(DespachosDbContext db, ICurrentCompanyAccessor company)
    {
        _db = db;
        _company = company;
    }

    private Guid CompanyId => _company.CompanyId;

    public async Task<DocumentReport> GetAsync(DocumentReportFilter f)
    {
        if (f.To < f.From) throw new InvalidOperationException("La fecha \"hasta\" no puede ser anterior a \"desde\".");
        if (f.To.DayNumber - f.From.DayNumber > IDocumentReportService.MaxRangeDays)
            throw new InvalidOperationException($"El rango de fechas no puede superar {IDocumentReportService.MaxRangeDays} días.");

        var manifests = _db.Manifests.AsNoTracking().Where(m =>
            m.CompanyId == CompanyId && m.DispatchDate >= f.From && m.DispatchDate <= f.To
            && (m.Status == ManifestStatuses.Dispatched || m.Status == ManifestStatuses.Returned
                || m.Status == ManifestStatuses.Closed));
        if (f.CarrierId is { } carrierId) manifests = manifests.Where(m => m.CarrierId == carrierId);

        var docs = _db.ManifestDocuments.AsNoTracking().Where(d => d.CompanyId == CompanyId);
        if (!string.IsNullOrWhiteSpace(f.DeliveryStatus)) docs = docs.Where(d => d.DeliveryStatus == f.DeliveryStatus);

        var rows = await (from d in docs
                          join m in manifests on d.ManifestId equals m.Id
                          select new { M = m, D = d }).ToListAsync();

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var term = f.Search.Trim();
            var number = long.TryParse(term, out var n) ? n : (long?)null;
            rows = rows.Where(r => (number != null && (r.D.Folio == number || r.M.Number == number))
                                   || r.D.CustomerName.Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToList();
        }

        var carriers = await _db.Carriers.AsNoTracking().Where(c => c.CompanyId == CompanyId).ToDictionaryAsync(c => c.Id, c => c.Name);
        var shifts = await _db.Shifts.AsNoTracking().Where(s => s.CompanyId == CompanyId).ToDictionaryAsync(s => s.Id, s => s.Name);
        var reasons = await _db.RejectionReasons.AsNoTracking().Where(r => r.CompanyId == CompanyId).ToDictionaryAsync(r => r.Id, r => r.Name);
        var communeIds = rows.Where(r => r.D.DestinationCommuneId != null).Select(r => r.D.DestinationCommuneId!.Value).Distinct().ToList();
        var communes = await _db.Communes.AsNoTracking().Where(c => communeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);

        var result = rows
            .GroupBy(r => r.M.Id)
            .Select(g =>
            {
                var m = g.First().M;
                var documents = g.Select(r => r.D).OrderBy(d => d.Folio).Select(d => new DocumentReportRow(
                    d.Id, d.DocumentKind, d.SiiDocumentType, d.Folio, d.CustomerName,
                    d.DestinationCommuneId is { } cid && communes.TryGetValue(cid, out var cn) ? cn : d.DestinationCommuneName,
                    d.Units, d.Packages, d.TotalAmount, d.DeliveryStatus,
                    d.RejectionReasonId is { } rid ? reasons.GetValueOrDefault(rid) : null, d.Comments, d.ReturnedAt)).ToList();
                return new DocumentReportManifest(
                    m.Id, m.Number, m.DispatchDate, m.Status, carriers.GetValueOrDefault(m.CarrierId, "—"),
                    m.ShiftId is { } sid ? shifts.GetValueOrDefault(sid) : null, m.Plate, m.DriverName, m.DepartedAt,
                    m.ReturnedAt, documents);
            })
            .OrderBy(m => m.DispatchDate).ThenBy(m => m.Number)
            .ToList();
        return new DocumentReport(result);
    }

    public async Task<byte[]> ExportExcelAsync(DocumentReportFilter f)
    {
        var r = await GetAsync(f);
        static string Status(string s) => DespachosUi.StatusLabel(DespachosUi.StatusKey(s));
        static string Local(DateTime? utc) => utc is { } u ? ChileClock.ToLocal(u).ToString("dd-MM-yyyy HH:mm") : "";

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Documentos");
        string[] headers =
        {
            "Manifiesto", "Fecha despacho", "Estado manifiesto", "Transportista", "Turno", "Patente", "Chofer", "Salida",
            "Tipo", "Folio", "Cliente", "Comuna", "Unidades", "Bultos", "Monto", "Estado documento", "Motivo",
            "Comentario", "Retorno registrado",
        };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
        var row = 2;
        foreach (var m in r.Manifests)
        {
            foreach (var d in m.Documents)
            {
                ws.Cell(row, 1).Value = m.Number;
                ws.Cell(row, 2).Value = m.DispatchDate.ToDateTime(TimeOnly.MinValue);
                ws.Cell(row, 2).Style.DateFormat.Format = "dd-MM-yyyy";
                ws.Cell(row, 3).Value = Status(m.Status);
                ws.Cell(row, 4).Value = m.Carrier;
                ws.Cell(row, 5).Value = m.Shift ?? "";
                ws.Cell(row, 6).Value = m.Plate ?? "";
                ws.Cell(row, 7).Value = m.Driver ?? "";
                ws.Cell(row, 8).Value = Local(m.DepartedAt);
                ws.Cell(row, 9).Value = DocumentKinds.Label(d.DocumentKind);
                ws.Cell(row, 10).Value = d.Folio;
                ws.Cell(row, 11).Value = d.CustomerName;
                ws.Cell(row, 12).Value = d.Commune ?? "";
                ws.Cell(row, 13).Value = d.Units;
                ws.Cell(row, 14).Value = d.Packages;
                ws.Cell(row, 15).Value = d.Amount;
                ws.Cell(row, 15).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, 16).Value = Status(d.DeliveryStatus);
                ws.Cell(row, 17).Value = d.RejectionReason ?? "";
                ws.Cell(row, 18).Value = d.Comments ?? "";
                ws.Cell(row, 19).Value = Local(d.ReturnedAt);
                row++;
            }
        }
        if (row > 2)
        {
            ws.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
            ws.SheetView.FreezeRows(1);
        }
        ws.Columns().AdjustToContents(1, Math.Min(row, 500));

        var sum = wb.AddWorksheet("Resumen");
        sum.Cell(1, 1).Value = $"Documentos despachados {f.From:dd-MM-yyyy} al {f.To:dd-MM-yyyy}";
        sum.Cell(1, 1).Style.Font.Bold = true;
        sum.Cell(1, 1).Style.Font.FontSize = 14;
        var s = 3;
        void Line(string label, XLCellValue value)
        {
            sum.Cell(s, 1).Value = label;
            sum.Cell(s, 1).Style.Font.Bold = true;
            sum.Cell(s, 2).Value = value;
            s++;
        }
        Line("Manifiestos", r.Manifests.Count);
        Line("Documentos", r.TotalDocuments);
        Line("Unidades", r.TotalUnits);
        Line("Bultos", r.TotalPackages);
        Line("Monto", r.TotalAmount);
        sum.Cell(s - 1, 2).Style.NumberFormat.Format = "#,##0";
        s++;
        sum.Cell(s, 1).Value = "Estado documento";
        sum.Cell(s, 2).Value = "Documentos";
        sum.Row(s).Style.Font.Bold = true;
        s++;
        foreach (var (status, count) in r.CountByStatus.OrderByDescending(x => x.Value))
        {
            sum.Cell(s, 1).Value = Status(status);
            sum.Cell(s, 2).Value = count;
            s++;
        }
        sum.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
