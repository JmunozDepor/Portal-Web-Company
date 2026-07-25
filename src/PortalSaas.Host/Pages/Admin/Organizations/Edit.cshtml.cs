using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class EditModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public EditModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IEnumerable<string> Modes => OrganizationMode.All;
    public IEnumerable<string> Statuses => OrganizationStatus.All;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var organization = await _db.Organizations.FindAsync(id);
        if (organization is null)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Id = organization.Id,
            LegalName = organization.LegalName,
            Slug = organization.Slug,
            TaxId = organization.TaxId,
            Country = organization.Country,
            Mode = organization.Mode,
            Status = organization.Status,
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var organization = await _db.Organizations.FindAsync(Input.Id);
        if (organization is null)
        {
            return NotFound();
        }

        var slug = Input.Slug.Trim().ToLowerInvariant();
        var slugEnUso = await _db.Organizations.AnyAsync(o => o.Slug == slug && o.Id != Input.Id);
        if (slugEnUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Slug)}", "Ya existe otra organización con ese slug.");
            return Page();
        }

        organization.LegalName = Input.LegalName.Trim();
        organization.Slug = slug;
        organization.TaxId = string.IsNullOrWhiteSpace(Input.TaxId) ? null : Input.TaxId.Trim();
        organization.Country = Input.Country.Trim();
        organization.Mode = Input.Mode;
        organization.Status = Input.Status;

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/Index");
    }

    public sealed class InputModel
    {
        public Guid Id { get; set; }

        [Required(ErrorMessage = "Ingresa el nombre legal.")]
        [Display(Name = "Nombre legal")]
        public string LegalName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa el slug.")]
        [RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Solo minúsculas, dígitos y guiones.")]
        [Display(Name = "Slug")]
        public string Slug { get; set; } = string.Empty;

        [Display(Name = "RUT / identificación tributaria")]
        public string? TaxId { get; set; }

        [Required(ErrorMessage = "Ingresa el país.")]
        [Display(Name = "País")]
        public string Country { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Modo")]
        public string Mode { get; set; } = OrganizationMode.Saas;

        [Required]
        [Display(Name = "Estado")]
        public string Status { get; set; } = OrganizationStatus.Trial;
    }
}
