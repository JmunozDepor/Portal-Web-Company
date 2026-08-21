using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Pages.Transacciones;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly IWmsTransaccionService _service;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IWmsTransaccionService service, ICurrentCompanyAccessor currentCompany)
    {
        _service = service;
        _currentCompany = currentCompany;
    }

    [BindProperty(SupportsGet = true)]
    public WmsTipoTransaccion Tipo { get; set; } = WmsTipoTransaccion.EnvioProducto;

    [BindProperty(SupportsGet = true)]
    public string? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Documento { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

    public WmsPagedResult<WmsTransaccionRow> Resultado { get; private set; } = new();

    public string TipoLabel => WmsTipoTransaccionInfo.Labels[Tipo];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Resultado = await _service.BuscarAsync(_currentCompany.CompanyId, new WmsTransaccionFiltro
        {
            Tipo = Tipo,
            Estado = Estado,
            Documento = Documento,
            Page = Page,
        }, ct);
    }

    public async Task<IActionResult> OnPostResetearAsync(WmsTipoTransaccion tipo, long[] lineIds, CancellationToken ct)
    {
        try
        {
            await _service.ResetearAsync(_currentCompany.CompanyId, tipo, lineIds, ct);
            SuccessMessage = $"{lineIds.Length} registro(s) reseteado(s) a Pendiente.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { tipo });
    }
}
