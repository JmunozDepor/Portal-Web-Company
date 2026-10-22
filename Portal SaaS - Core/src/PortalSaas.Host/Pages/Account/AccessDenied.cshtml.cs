using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PortalSaas.Host.Pages.Account;

/// <summary>
/// Destino de AccessDeniedPath del esquema tenant (ver Program.cs) -- un Forbid() de
/// cualquier página/plugin cae acá. Antes apuntaba a /Account/Login: el usuario, con la
/// sesión todavía viva, veía el formulario de login como si lo hubieran deslogueado,
/// reintentaba y podía terminar bloqueado por intentos fallidos (bug real, 2026-10-21).
/// </summary>
[Authorize]
public class AccessDeniedModel : PageModel
{
    public void OnGet()
    {
    }
}
