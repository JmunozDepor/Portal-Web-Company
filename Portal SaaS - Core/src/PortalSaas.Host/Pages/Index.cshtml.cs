using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PortalSaas.Host.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true
            ? RedirectToPage("/Home/Index")
            : RedirectToPage("/Account/Login");
}
