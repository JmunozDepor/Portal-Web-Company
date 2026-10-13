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

    // Bloquea el borrado si cuelga de esta instancia alguna Company (perdería su conexión
    // SAP) o la base de algún módulo (la FK es Restrict: sin este chequeo, el borrado
    // reventaría con un DbUpdateException).
    public async Task<IActionResult> OnPostDeleteAsync(Guid organizationId, long id)
    {
        if (await _db.Companies.AnyAsync(c => c.InstanceId == id))
        {
            ErrorMessage = "No se puede eliminar: hay compañías creadas sobre esta instancia.";
            return RedirectToPage(new { organizationId });
        }

        if (await _db.CompanyModuleConnections.AnyAsync(b => b.InstanceId == id))
        {
            ErrorMessage = "No se puede eliminar: hay módulos que usan una base de datos de esta instancia (ver Conexiones externas de cada compañía).";
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
