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

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        MenuGroups = await _db.MenuGroups
            .Include(g => g.MenuGroupItems)
            .OrderBy(g => g.Name)
            .ToListAsync();
    }

    // Bloquea el borrado si algún usuario ya tiene este grupo asignado (UserMenuGroup)
    // -- borrarlo ahí lo dejaría sin acceso en silencio. MenuGroupItem se cascadea
    // sola, no bloquea.
    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        var inUse = await _db.UserMenuGroups.AnyAsync(g => g.MenuGroupId == id);
        if (inUse)
        {
            ErrorMessage = "No se puede eliminar: el grupo está asignado a al menos un usuario.";
            return RedirectToPage();
        }

        var group = await _db.MenuGroups.Include(g => g.MenuGroupItems).FirstOrDefaultAsync(g => g.Id == id);
        if (group is not null)
        {
            _db.MenuGroups.Remove(group);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }
}
