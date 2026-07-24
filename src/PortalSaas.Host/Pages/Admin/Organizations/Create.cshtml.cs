using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class CreateModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public CreateModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IEnumerable<string> Modes => OrganizationMode.All;

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var slug = Input.Slug.Trim().ToLowerInvariant();
        var slugEnUso = await _db.Organizations.AnyAsync(o => o.Slug == slug);
        if (slugEnUso)
        {
            ModelState.AddModelError(nameof(Input.Slug), "Ya existe una organización con ese slug.");
            return Page();
        }

        var organization = new Organization
        {
            LegalName = Input.LegalName.Trim(),
            Slug = slug,
            TaxId = string.IsNullOrWhiteSpace(Input.TaxId) ? null : Input.TaxId.Trim(),
            Country = Input.Country.Trim(),
            Mode = Input.Mode,
        };

        _db.Organizations.Add(organization);
        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/Index");
    }

    public sealed class InputModel
    {
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
    }
}
