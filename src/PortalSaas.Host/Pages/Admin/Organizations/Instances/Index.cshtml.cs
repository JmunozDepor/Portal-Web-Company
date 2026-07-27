using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Instances;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public IndexModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public Organization Organization { get; private set; } = null!;
    public List<Instance> Instances { get; private set; } = [];

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
        Instances = await _db.Instances
            .Where(i => i.OrganizationId == organizationId)
            .OrderBy(i => i.Name)
            .ToListAsync();

        return Page();
    }

    // Bloquea el borrado si ya cuelga alguna Company de esta instancia -- borrarla
    // dejaría esas compañías sin cómo resolver su conexión SAP.
    public async Task<IActionResult> OnPostDeleteAsync(Guid organizationId, long id)
    {
        var inUse = await _db.Companies.AnyAsync(c => c.InstanceId == id);
        if (inUse)
        {
            ErrorMessage = "No se puede eliminar: hay compañías creadas sobre esta instancia.";
            return RedirectToPage(new { organizationId });
        }

        var instance = await _db.Instances.FirstOrDefaultAsync(i => i.Id == id && i.OrganizationId == organizationId);
        if (instance is not null)
        {
            _db.Instances.Remove(instance);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage(new { organizationId });
    }
}
