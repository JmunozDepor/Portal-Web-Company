using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Reportes;

/// <summary>
/// Reporte de cierre: rendiciones Approved en un período, exportable a CSV para carga
/// manual a SAP B1. Sin log de trazabilidad del export todavía -- un export simple
/// basta para esta fase (ver PENDIENTE.md del repo).
/// </summary>
public sealed class CierreModel : RendicionesPageModelBase
{
    private readonly IClosingReportService _report;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public CierreModel(IClosingReportService report, ICurrentCompanyAccessor currentCompany)
    {
        _report = report;
        _currentCompany = currentCompany;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime From { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty(SupportsGet = true)]
    public DateTime To { get; set; } = DateTime.Today;

    public IReadOnlyList<ClosingReportLine> Lines { get; private set; } = Array.Empty<ClosingReportLine>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Lines = await _report.GenerateAsync(_currentCompany.CompanyId, From, To, ct);
    }

    public async Task<IActionResult> OnGetExportarAsync(CancellationToken ct)
    {
        var lines = await _report.GenerateAsync(_currentCompany.CompanyId, From, To, ct);

        var csv = new StringBuilder();
        csv.AppendLine("Rendicion,Ronda,Colaborador,CentroCostoCodigo,CentroCostoNombre,TipoGasto,Fecha,Monto,Moneda,Glosa");
        foreach (var l in lines)
        {
            csv.AppendLine(string.Join(",",
                l.ExpenseReportId,
                l.Round,
                CsvEscape(l.EmployeeName),
                CsvEscape(l.CostCenterCode),
                CsvEscape(l.CostCenterName),
                CsvEscape(l.ExpenseTypeName),
                l.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                l.Amount.ToString("F2", CultureInfo.InvariantCulture),
                l.Currency,
                CsvEscape(l.Notes)));
        }

        var fileName = $"cierre-rendiciones-{From:yyyyMMdd}-{To:yyyyMMdd}.csv";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", fileName);
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
