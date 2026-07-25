using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
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
    public List<SelectListItem> Plans { get; private set; } = [];

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
        await CargarPlanesAsync();
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
        await CargarPlanesAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var planValido = await _db.Plans.AnyAsync(p => p.Id == Input.PlanId && p.IsActive);
        if (!planValido)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.PlanId)}", "Plan inválido.");
            return Page();
        }

        // La clave de activación la genera el servidor -- no es algo que un admin
        // "elija", es un secreto aleatorio de alta entropía (mismo criterio que el
        // token de recuperación de contraseña).
        var activationKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

        _db.OnPremiseLicenses.Add(new OnPremiseLicense
        {
            OrganizationId = organizationId,
            PlanId = Input.PlanId,
            ActivationKey = activationKey,
            InstallationFingerprint = string.IsNullOrWhiteSpace(Input.InstallationFingerprint) ? null : Input.InstallationFingerprint.Trim(),
            ExpiresAt = new DateTimeOffset(DateOnly.Parse(Input.ExpiresAt).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        });

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/Licenses/Index", new { organizationId });
    }

    private async Task CargarPlanesAsync()
    {
        Plans = await _db.Plans
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new SelectListItem(p.Name, p.Id.ToString()))
            .ToListAsync();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Selecciona el plan.")]
        [Display(Name = "Plan")]
        public long PlanId { get; set; }

        [Required(ErrorMessage = "Ingresa la fecha de vencimiento.")]
        [Display(Name = "Vence")]
        public string ExpiresAt { get; set; } = string.Empty;

        [Display(Name = "Huella de instalación (opcional)")]
        public string? InstallationFingerprint { get; set; }
    }
}
