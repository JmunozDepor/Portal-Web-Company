using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.PlatformModules;

/// <summary>
/// Catálogo comercial de módulos vendibles (PlatformModule) -- de qué depende
/// PlanModule (qué incluye cada plan) y OrganizationModule (add-ons por
/// organización), consumidos por IModuleAccessService para filtrar el árbol de menú
/// (ver MenuNavigationService, CLAUDE.md). Antes de esta pantalla la tabla no tenía
/// ninguna forma de cargarse salvo SQL directo.
/// </summary>
[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public IndexModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public List<PlatformModule> Modules { get; private set; } = [];

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Modules = await _db.PlatformModules
            .Include(m => m.ExclusiveOrganization)
            .OrderBy(m => m.Code)
            .ToListAsync();
    }

    // Bloquea el borrado si el módulo ya está incluido en un plan o contratado como
    // add-on por alguna organización -- borrarlo ahí cambiaría en silencio qué puede
    // ver cada organización (IModuleAccessService).
    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        var inUse = await _db.PlanModules.AnyAsync(pm => pm.ModuleId == id)
            || await _db.OrganizationModules.AnyAsync(om => om.ModuleId == id);
        if (inUse)
        {
            ErrorMessage = "No se puede eliminar: el módulo está incluido en al menos un plan u organización.";
            return RedirectToPage();
        }

        var module = await _db.PlatformModules.FindAsync(id);
        if (module is not null)
        {
            _db.PlatformModules.Remove(module);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }
}
