using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Licenses;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class EditModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public EditModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public Organization Organization { get; private set; } = null!;
    public string ActivationKey { get; private set; } = string.Empty;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var license = await _db.OnPremiseLicenses
            .Include(l => l.Organization)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (license is null)
        {
            return NotFound();
        }

        Organization = license.Organization;
        ActivationKey = license.ActivationKey;
        Input = new InputModel
        {
            Id = license.Id,
            OrganizationId = license.OrganizationId,
            Status = license.Status,
            ExpiresAt = DateOnly.FromDateTime(license.ExpiresAt.UtcDateTime).ToString("yyyy-MM-dd"),
            InstallationFingerprint = license.InstallationFingerprint,
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var license = await _db.OnPremiseLicenses
            .Include(l => l.Organization)
            .FirstOrDefaultAsync(l => l.Id == Input.Id);

        if (license is null)
        {
            return NotFound();
        }

        Organization = license.Organization;
        ActivationKey = license.ActivationKey;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        license.Status = Input.Status;
        license.ExpiresAt = new DateTimeOffset(DateOnly.Parse(Input.ExpiresAt).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        license.InstallationFingerprint = string.IsNullOrWhiteSpace(Input.InstallationFingerprint) ? null : Input.InstallationFingerprint.Trim();

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/Licenses/Index", new { organizationId = Input.OrganizationId });
    }

    public sealed class InputModel
    {
        public long Id { get; set; }
        public Guid OrganizationId { get; set; }

        [Required]
        [Display(Name = "Estado")]
        public string Status { get; set; } = OnPremiseLicenseStatus.Active;

        [Required(ErrorMessage = "Ingresa la fecha de vencimiento.")]
        [Display(Name = "Vence")]
        public string ExpiresAt { get; set; } = string.Empty;

        [Display(Name = "Huella de instalación (opcional)")]
        public string? InstallationFingerprint { get; set; }
    }
}
