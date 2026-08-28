using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Host.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly IUserSessionService _sessions;

    public LogoutModel(IUserSessionService sessions)
    {
        _sessions = sessions;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Cierre normal -- revoca la fila de "clientes conectados" de una, para que no
        // siga apareciendo activa en el backoffice hasta que la cookie expirara sola.
        var sessionToken = User.FindFirst("SessionToken")?.Value;
        if (sessionToken is not null)
        {
            await _sessions.RevokeByTokenAsync(sessionToken);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Account/Login");
    }
}
