using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.MenuGroups;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public IndexModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public List<MenuGroup> MenuGroups { get; private set; } = [];

    public async Task OnGetAsync()
    {
        MenuGroups = await _db.MenuGroups
            .Include(g => g.MenuGroupItems)
            .OrderBy(g => g.Name)
            .ToListAsync();
    }
}
