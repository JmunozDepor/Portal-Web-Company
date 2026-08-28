using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Profiles;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public IndexModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public List<Profile> Profiles { get; private set; } = [];

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Profiles = await _db.Profiles
            .Include(p => p.ProfileActions)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    // Bloquea el borrado si algún usuario ya tiene este Perfil asignado a un nodo de
    // menú (UserMenuProfile) -- borrarlo ahí lo dejaría sin acceso en silencio.
    // ProfileAction se cascadea sola, no bloquea.
    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        var inUse = await _db.UserMenuProfiles.AnyAsync(p => p.ProfileId == id);
        if (inUse)
        {
            ErrorMessage = "No se puede eliminar: el perfil está asignado a al menos un usuario.";
            return RedirectToPage();
        }

        var profile = await _db.Profiles.Include(p => p.ProfileActions).FirstOrDefaultAsync(p => p.Id == id);
        if (profile is not null)
        {
            _db.Profiles.Remove(profile);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }
}
