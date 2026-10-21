using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.Reportes;

/// <summary>Reporte mensual de despachos. Menú "reportes": VIEW ve el reporte, EXPORT descarga el Excel.</summary>
public sealed class MensualModel : DespachosPageModelBase
{
    private readonly IDispatchReportService _report;

    public MensualModel(IDispatchReportService report, ICurrentUserContext currentUser) : base(currentUser)
    {
        _report = report;
    }

    protected override string MenuCode => "reportes";

    /// <summary>Período "yyyy-MM"; vacío = mes actual (hora Chile).</summary>
    [BindProperty(SupportsGet = true)] public string? Period { get; set; }

    public int Year { get; private set; }
    public int Month { get; private set; }
    public string PeriodText => $"{Year:0000}-{Month:00}";
    public bool CanExport { get; private set; }
    public MonthlyDispatchReport? Report { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        ResolvePeriod();
        CanExport = await HasActionAsync(PortalActions.Export);
        try { Report = await _report.GetMonthlyAsync(Year, Month); }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return Page();
    }

    public async Task<IActionResult> OnGetExportarAsync()
    {
        if (!await HasActionAsync(PortalActions.Export)) return Forbid();
        ResolvePeriod();
        try
        {
            var bytes = await _report.ExportMonthlyExcelAsync(Year, Month);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"despachos-{Year:0000}-{Month:00}.xlsx");
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage(new { period = PeriodText });
        }
    }

    private void ResolvePeriod()
    {
        if (DateTime.TryParseExact(Period?.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            Year = d.Year;
            Month = d.Month;
        }
        else
        {
            var now = ChileClock.Now;
            Year = now.Year;
            Month = now.Month;
        }
    }
}
