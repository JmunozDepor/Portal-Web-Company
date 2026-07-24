using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Licenses;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class CreateModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public CreateModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public Organization Organization { get; private set; } = null!;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
        Input.ExpiresAt = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // La clave de activación la genera el servidor -- no es algo que un admin
        // "elija", es un secreto aleatorio de alta entropía (mismo criterio que el
        // token de recuperación de contraseña).
        var activationKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

        _db.OnPremiseLicenses.Add(new OnPremiseLicense
        {
            OrganizationId = organizationId,
            ActivationKey = activationKey,
            InstallationFingerprint = string.IsNullOrWhiteSpace(Input.InstallationFingerprint) ? null : Input.InstallationFingerprint.Trim(),
            ExpiresAt = new DateTimeOffset(DateOnly.Parse(Input.ExpiresAt).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        });

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/Licenses/Index", new { organizationId });
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Ingresa la fecha de vencimiento.")]
        [Display(Name = "Vence")]
        public string ExpiresAt { get; set; } = string.Empty;

        [Display(Name = "Huella de instalación (opcional)")]
        public string? InstallationFingerprint { get; set; }
    }
}
