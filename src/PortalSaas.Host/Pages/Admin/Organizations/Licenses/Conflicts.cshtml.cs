using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Licenses;

/// <summary>
/// Resolución manual de conflictos de InstallationFingerprint -- ver
/// OnPremiseLicenseConflict y LicenseActivationService. "Aceptar" rebindea la
/// licencia al fingerprint reportado (ej. el cliente migró de servidor a propósito);
/// "Rechazar" solo marca el conflicto como visto, sin tocar el fingerprint vigente
/// (ej. era un intento no autorizado).
/// </summary>
[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class ConflictsModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public ConflictsModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public OnPremiseLicense License { get; private set; } = null!;
    public Organization Organization { get; private set; } = null!;
    public List<OnPremiseLicenseConflict> UnresolvedConflicts { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var license = await _db.OnPremiseLicenses
            .Include(l => l.Organization)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (license is null)
        {
            return NotFound();
        }

        License = license;
        Organization = license.Organization;
        UnresolvedConflicts = await _db.OnPremiseLicenseConflicts
            .Where(c => c.OnPremiseLicenseId == id && !c.Resolved)
            .OrderByDescending(c => c.ReportedAt)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAcceptAsync(long conflictId)
    {
        var conflict = await _db.OnPremiseLicenseConflicts
            .Include(c => c.OnPremiseLicense)
            .FirstOrDefaultAsync(c => c.Id == conflictId);

        if (conflict is null)
        {
            return NotFound();
        }

        // Rebindea la licencia al fingerprint reportado por este conflicto -- la
        // instalación anterior queda automáticamente sin acceso en su próximo
        // heartbeat, porque su fingerprint ya no coincide con el vigente.
        conflict.OnPremiseLicense.InstallationFingerprint = conflict.ReportedFingerprint;
        MarcarResuelto(conflict);

        // El resto de los conflictos pendientes de esta licencia quedan obsoletos --
        // ya se decidió cuál instalación es la vigente.
        var otros = await _db.OnPremiseLicenseConflicts
            .Where(c => c.OnPremiseLicenseId == conflict.OnPremiseLicenseId && !c.Resolved && c.Id != conflictId)
            .ToListAsync();
        foreach (var otro in otros)
        {
            MarcarResuelto(otro);
        }

        await _db.SaveChangesAsync();
        return RedirectToPage(new { id = conflict.OnPremiseLicenseId });
    }

    public async Task<IActionResult> OnPostRejectAsync(long conflictId)
    {
        var conflict = await _db.OnPremiseLicenseConflicts.FindAsync(conflictId);
        if (conflict is null)
        {
            return NotFound();
        }

        MarcarResuelto(conflict);
        await _db.SaveChangesAsync();
        return RedirectToPage(new { id = conflict.OnPremiseLicenseId });
    }

    private void MarcarResuelto(OnPremiseLicenseConflict conflict)
    {
        conflict.Resolved = true;
        conflict.ResolvedAt = DateTimeOffset.UtcNow;
        conflict.ResolvedByAdminEmail = User.Identity?.Name;
    }
}
