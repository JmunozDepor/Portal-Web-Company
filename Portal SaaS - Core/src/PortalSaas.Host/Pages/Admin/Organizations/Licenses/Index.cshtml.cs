using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Comercial.Licenciamiento;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Licenses;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ILicenseHeartbeatService? _heartbeatService;

    public IndexModel(PortalSaasDbContext db, ILicenseHeartbeatService? heartbeatService = null)
    {
        _db = db;
        _heartbeatService = heartbeatService;
    }

    public Organization Organization { get; private set; } = null!;
    public List<OnPremiseLicense> Licenses { get; private set; } = [];
    public Dictionary<long, int> UnresolvedConflictsByLicenseId { get; private set; } = [];

    // Solo la instalación on-premise tiene ILicenseHeartbeatService registrado (ver
    // Program.cs, condicionado a Licensing:Role=OnPremise) -- el botón "Validar ahora"
    // no tiene sentido del lado Central (esa es la que RESPONDE heartbeats, no la que
    // los hace).
    public bool PuedeValidarManualmente => _heartbeatService is not null;

    public async Task<IActionResult> OnGetAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
        await CargarLicenciasAsync(organizationId);

        return Page();
    }

    public async Task<IActionResult> OnPostValidarAsync(Guid organizationId)
    {
        if (_heartbeatService is null)
        {
            return NotFound();
        }

        var result = await _heartbeatService.HeartbeatOnceAsync();
        TempData[result.Outcome == LicenseHeartbeatOutcome.Success ? "MensajeExito" : "MensajeError"] =
            $"Validación manual: {result.Message}";

        return RedirectToPage("/Admin/Organizations/Licenses/Index", new { organizationId });
    }

    private async Task CargarLicenciasAsync(Guid organizationId)
    {
        Licenses = await _db.OnPremiseLicenses
            .Include(l => l.Plan)
            .Where(l => l.OrganizationId == organizationId)
            .OrderByDescending(l => l.IssuedAt)
            .ToListAsync();

        var licenseIds = Licenses.Select(l => l.Id).ToList();
        UnresolvedConflictsByLicenseId = await _db.OnPremiseLicenseConflicts
            .Where(c => licenseIds.Contains(c.OnPremiseLicenseId) && !c.Resolved)
            .GroupBy(c => c.OnPremiseLicenseId)
            .Select(g => new { LicenseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LicenseId, x => x.Count);
    }
}
