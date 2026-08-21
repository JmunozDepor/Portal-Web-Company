using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Pages.Confirmaciones;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly IWmsConfirmacionService _service;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IWmsConfirmacionService service, ICurrentCompanyAccessor currentCompany)
    {
        _service = service;
        _currentCompany = currentCompany;
    }

    [BindProperty(SupportsGet = true)]
    public WmsTipoTransaccion Tipo { get; set; } = WmsTipoTransaccion.ConfirmacionOrdenes;

    [BindProperty(SupportsGet = true)]
    public string? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Documento { get; set; }

    public WmsPagedResult<WmsConfirmacionRow> Resultado { get; private set; } = new();

    public string TipoLabel => WmsTipoTransaccionInfo.Labels[Tipo];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Resultado = await _service.BuscarAsync(_currentCompany.CompanyId, new WmsConfirmacionFiltro { Tipo = Tipo, Estado = Estado, Documento = Documento }, ct);
    }

    public async Task<IActionResult> OnPostResetearAsync(WmsTipoTransaccion tipo, string documento, CancellationToken ct)
    {
        try
        {
            await _service.ResetearAsync(_currentCompany.CompanyId, tipo, documento, ct);
            SuccessMessage = $"Documento {documento} reseteado a Pendiente.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { tipo });
    }
}
