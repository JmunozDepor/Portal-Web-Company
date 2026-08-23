using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Pages.Dashboard;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly IWmsDashboardService _dashboard;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IWmsDashboardService dashboard, ICurrentCompanyAccessor currentCompany)
    {
        _dashboard = dashboard;
        _currentCompany = currentCompany;
    }

    public WmsDashboardResumen Resumen { get; private set; } = new();
    public WmsDashboardResumen Historico { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public int DiasAtras { get; set; } = 1;

    public async Task OnGetAsync(CancellationToken ct)
    {
        var desde = DateTime.UtcNow.AddDays(-Math.Max(1, DiasAtras));
        Resumen = await _dashboard.ObtenerResumenAsync(_currentCompany.CompanyId, desde, ct);
        Historico = await _dashboard.ObtenerResumenAsync(_currentCompany.CompanyId, DateTime.UtcNow.AddYears(-5), ct);
    }
}
