using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Account;

/// <summary>
/// Cambio rápido de tema desde la paleta de colores del topbar
/// (_LayoutMaestro.cshtml, a la izquierda del menú de usuario). Solo POST -- no tiene
/// vista propia, es un endpoint de acción pura que redirige de vuelta a returnUrl,
/// mismo patrón que SwitchCompany.
///
/// Persiste en la cuenta vía IUserPreferenceService -- la MISMA fuente de verdad que
/// /Home/Preferences, NO localStorage. _LayoutMaestro aplica el tema server-side desde
/// esa preferencia en cada request; si el cambio del topbar solo tocara localStorage,
/// la siguiente navegación lo revertiría (bug real ya documentado, 2026-08-19).
/// </summary>
[Authorize]
public class SetThemeModel : PageModel
{
    private readonly IUserPreferenceService _preferences;

    public SetThemeModel(IUserPreferenceService preferences)
    {
        _preferences = preferences;
    }

    public IActionResult OnGet() => RedirectToPage("/Home/Preferences");

    public async Task<IActionResult> OnPostAsync(string? theme, string? returnUrl, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(theme) && UserThemePreference.All.Contains(theme))
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var current = await _preferences.GetOrCreateDefaultAsync(userId, ct);

            if (current.Theme != theme)
            {
                await _preferences.UpdateAsync(userId, current with { Theme = theme }, ct);
            }
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) && returnUrl is not null ? returnUrl : Url.Content("~/Home/Index"));
    }
}
