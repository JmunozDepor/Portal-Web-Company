using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Host.Infraestructura;

namespace PortalSaas.Host.Pages.Admin.Sistema;

/// <summary>
/// Reiniciar el Host desde el backoffice -- pedido puntual para que un plugin recién
/// importado (ver Pages/Admin/PlatformModules/Import.cshtml.cs) quede activo sin que
/// alguien tenga que pararse en el servidor a mano. Ver ApplicationRestartService para
/// el detalle del watchdog.
/// </summary>
[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly IApplicationRestartService _restartService;

    public IndexModel(IApplicationRestartService restartService)
    {
        _restartService = restartService;
    }

    public int ProcessId => Environment.ProcessId;

    public IActionResult OnPostRestartAsync()
    {
        _restartService.ScheduleRestart(TimeSpan.FromSeconds(2));
        TempData["Mensaje"] = "Reinicio programado -- el servidor se detiene en unos segundos y vuelve a levantar solo. " +
            "Esta pestaña puede mostrar un error de conexión mientras tanto, es esperable.";
        return RedirectToPage();
    }
}
