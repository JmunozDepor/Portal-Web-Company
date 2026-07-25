using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.MenuGroups;

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
        var nombreEnUso = await _db.MenuGroups.AnyAsync(g => g.Name == name);
        if (nombreEnUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Name)}", "Ya existe un grupo de menú con ese nombre.");
            return Page();
        }

        var menuGroup = new MenuGroup
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
        };

        _db.MenuGroups.Add(menuGroup);
        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/MenuGroups/Edit", new { id = menuGroup.Id });
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Ingresa el nombre del grupo.")]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Descripción")]
        public string? Description { get; set; }
    }
}
