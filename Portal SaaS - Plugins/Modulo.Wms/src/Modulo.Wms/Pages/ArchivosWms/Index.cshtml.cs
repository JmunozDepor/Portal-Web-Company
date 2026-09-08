using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Pages.ArchivosWms;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly IWmsArchivoService _service;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IWmsArchivoService service, ICurrentCompanyAccessor currentCompany)
    {
        _service = service;
        _currentCompany = currentCompany;
    }

    [BindProperty(SupportsGet = true)]
    public string? TipoDoc { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    public WmsPagedResult<WmsOracleInboundStage> Resultado { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Resultado = await _service.ListarAsync(_currentCompany.CompanyId, TipoDoc, Estado, Pagina, 25, ct);
    }

    public async Task<IActionResult> OnPostReintentarAsync(long id, CancellationToken ct)
    {
        try
        {
            await _service.ReintentarAsync(_currentCompany.CompanyId, id, ct);
            SuccessMessage = "Archivo vuelto a Pendiente.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }
}
