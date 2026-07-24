using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PortalSaas.Host.Pages.Home;

[Authorize]
public class IndexModel : PageModel
{
    public string? Email { get; private set; }
    public string? OrganizationSlug { get; private set; }

    public void OnGet()
    {
        Email = User.FindFirstValue(ClaimTypes.Email);
        OrganizationSlug = User.FindFirstValue("OrganizationSlug");
    }
}
