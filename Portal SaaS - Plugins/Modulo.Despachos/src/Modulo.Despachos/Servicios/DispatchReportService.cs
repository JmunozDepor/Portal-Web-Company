using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Servicios;

public sealed class DispatchReportService : IDispatchReportService
{
    private const string NoShift = "Sin turno";

    private readonly DespachosDbContext _db;
    private readonly ICurrentCompanyAccessor _company;

    public DispatchReportService(DespachosDbContext db, ICurrentCompanyAccessor company)
    {
        _db = db;
        _company = company;
    }

    private Guid CompanyId => _company.CompanyId;

    public async Task<MonthlyDispatchReport> GetMonthlyAsync(int year, int month)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            throw new InvalidOperationException("El período indicado no es válido.");
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1);
        var rows = await _db.Manifests
            .Where(m => m.CompanyId == CompanyId && m.DispatchDate >= from && m.DispatchDate < to
                        && (m.Status == ManifestStatuses.Dispatched || m.Status == ManifestStatuses.Returned
                            || m.Status == ManifestStatuses.Closed))
            .Select(m => new { m.DispatchDate, m.ShiftId, m.CarrierId, m.CostFinal, m.TotalUnits, m.TotalDocuments, m.TotalPackages })
            .ToListAsync();
        var shifts = await _db.Shifts.Where(s => s.CompanyId == CompanyId).ToDictionaryAsync(s => s.Id, s => s.Name);
        var carriers = await _db.Carriers.Where(c => c.CompanyId == CompanyId).ToDictionaryAsync(c => c.Id, c => c.Name);

        var days = rows.GroupBy(r => r.DispatchDate).OrderBy(g => g.Key).Select(day =>
        {
            var details = day
                .GroupBy(r => (Shift: r.ShiftId is { } s ? shifts.GetValueOrDefault(s, NoShift) : NoShift,
                               Carrier: carriers.GetValueOrDefault(r.CarrierId, "—")))
                .OrderBy(g => g.Key.Shift, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(g => g.Key.Carrier, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => new DailyDetailRow(g.Key.Shift, g.Key.Carrier, g.Sum(x => x.CostFinal ?? 0m),
                    g.Sum(x => x.TotalUnits), g.Sum(x => x.TotalDocuments), g.Sum(x => x.TotalPackages)))
                .ToList();
            return new DailyRow(day.Key, details.Sum(d => d.Cost), details.Sum(d => d.Units),
                details.Sum(d => d.Documents), details.Sum(d => d.Packages), details);
        }).ToList();

        return new MonthlyDispatchReport(
            days.Sum(d => d.Cost), days.Sum(d => d.Units), days.Sum(d => d.Documents), days.Sum(d => d.Packages),
            days, rows.Count(r => r.CostFinal == null));
    }

    public async Task<byte[]> ExportMonthlyExcelAsync(int year, int month)
    {
        var r = await GetMonthlyAsync(year, month);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Resumen");
        ws.Cell(1, 1).Value = $"Despachos {year:0000}-{month:00}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = "Costo total";
        ws.Cell(2, 2).Value = r.TotalCost;
        ws.Cell(3, 1).Value = "Unidades";
        ws.Cell(3, 2).Value = r.TotalUnits;
        ws.Cell(4, 1).Value = "Documentos";
        ws.Cell(4, 2).Value = r.TotalDocuments;
        ws.Cell(5, 1).Value = "Bultos";
        ws.Cell(5, 2).Value = r.TotalPackages;
        ws.Cell(6, 1).Value = "Manifiestos sin costo";
        ws.Cell(6, 2).Value = r.ManifestsWithoutCost;
        ws.Range(2, 1, 6, 1).Style.Font.Bold = true;

        var row = 8;
        string[] headers = { "Fecha", "Turno", "Transportista", "Costo", "Unidades", "Documentos", "Bultos" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(row, i + 1).Value = headers[i];
        ws.Range(row, 1, row, 7).Style.Font.Bold = true;
        ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.LightGray;
        row++;

        foreach (var d in r.Days)
        {
            WriteRow(ws, row, d.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture), "Subtotal del día", "",
                d.Cost, d.Units, d.Documents, d.Packages);
            ws.Range(row, 1, row, 7).Style.Font.Bold = true;
            row++;
            foreach (var x in d.Details)
            {
                WriteRow(ws, row, "", x.ShiftName, x.CarrierName, x.Cost, x.Units, x.Documents, x.Packages);
                row++;
            }
        }
        WriteRow(ws, row, "Total", "", "", r.TotalCost, r.TotalUnits, r.TotalDocuments, r.TotalPackages);
        ws.Range(row, 1, row, 7).Style.Font.Bold = true;
        ws.Range(row, 1, row, 7).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        ws.Range(2, 2, 6, 2).Style.NumberFormat.Format = "#,##0";
        ws.Range(9, 4, row, 7).Style.NumberFormat.Format = "#,##0";
        ws.Column(1).Width = 22;
        ws.Columns(2, 7).AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteRow(IXLWorksheet ws, int row, string a, string b, string c,
        decimal cost, decimal units, int docs, int packages)
    {
        ws.Cell(row, 1).Value = a;
        ws.Cell(row, 2).Value = b;
        ws.Cell(row, 3).Value = c;
        ws.Cell(row, 4).Value = cost;
        ws.Cell(row, 5).Value = units;
        ws.Cell(row, 6).Value = docs;
        ws.Cell(row, 7).Value = packages;
    }
}
