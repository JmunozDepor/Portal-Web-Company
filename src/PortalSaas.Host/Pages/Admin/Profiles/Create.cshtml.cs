using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Profiles;

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

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var name = Input.Name.Trim();
        var nombreEnUso = await _db.Profiles.AnyAsync(p => p.Name == name);
        if (nombreEnUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Name)}", "Ya existe un perfil con ese nombre.");
            return Page();
        }

        var profile = new Profile
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
        };

        _db.Profiles.Add(profile);
        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Profiles/Edit", new { id = profile.Id });
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Ingresa el nombre del perfil.")]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Descripción")]
        public string? Description { get; set; }
    }
}
