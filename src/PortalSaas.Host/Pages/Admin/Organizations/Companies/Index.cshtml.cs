using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public IndexModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public Organization Organization { get; private set; } = null!;
    public List<Company> Companies { get; private set; } = [];
    public bool TieneInstancias { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
        Companies = await _db.Companies
            .Include(c => c.Instance)
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.Code)
            .ToListAsync();
        TieneInstancias = await _db.Instances.AnyAsync(i => i.OrganizationId == organizationId);

        return Page();
    }
}
