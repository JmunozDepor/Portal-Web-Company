using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Host.Infraestructura;

namespace PortalSaas.Host.Pages.Account;

/// <summary>
/// Cambia la compañía activa de la sesión SIN cerrar sesión -- a diferencia de
/// SelectCompany (paso obligatorio del login, solo corre una vez), esta página existe
/// para el selector del topbar (ver ViewComponents/CompanySwitcherViewComponent,
/// Pages/Shared/_Layout.cshtml) que deja cambiar de compañía en cualquier momento.
/// Solo POST -- no tiene vista propia, es un endpoint de acción pura que redirige de
/// vuelta a ReturnUrl (la página donde estaba el usuario cuando cambió).
/// </summary>
[Authorize]
public class SwitchCompanyModel : PageModel
{
    private readonly ICompanySessionActivator _activator;

    public SwitchCompanyModel(ICompanySessionActivator activator)
    {
        _activator = activator;
    }

    public async Task<IActionResult> OnPostAsync(Guid companyId, string? returnUrl)
    {
        var company = await _activator.TryActivateAsync(HttpContext, companyId);
        if (company is null)
        {
            // La sesión sigue con la compañía anterior a propósito (nunca dejar la
            // sesión sin compañía activa) -- pero antes esto fallaba en silencio: el
            // selector volvía a mostrar la compañía vieja sin ninguna pista de por qué
            // (bug real, 2026-08-08). TempData["Error"] lo levanta _Layout.cshtml sea
            // cual sea la página a la que se vuelva.
            TempData["Error"] = "No se pudo cambiar de compañía -- no tenés acceso a la compañía elegida, o ya no está activa.";
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) && returnUrl is not null ? returnUrl : Url.Content("~/Home/Index"));
    }
}
