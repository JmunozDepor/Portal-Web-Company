using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Profiles;

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

    public List<PermissionAction> AllActions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var profile = await _db.Profiles
            .Include(p => p.ProfileActions)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (profile is null)
        {
            return NotFound();
        }

        AllActions = await _db.Actions.OrderBy(a => a.Id).ToListAsync();

        Input = new InputModel
        {
            Id = profile.Id,
            Name = profile.Name,
            Description = profile.Description,
            SelectedActionIds = profile.ProfileActions.Select(pa => pa.ActionId).ToList(),
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var profile = await _db.Profiles
            .Include(p => p.ProfileActions)
            .FirstOrDefaultAsync(p => p.Id == Input.Id);
        if (profile is null)
        {
            return NotFound();
        }

        AllActions = await _db.Actions.OrderBy(a => a.Id).ToListAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var name = Input.Name.Trim();
        var nombreEnUso = await _db.Profiles.AnyAsync(p => p.Name == name && p.Id != Input.Id);
        if (nombreEnUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Name)}", "Ya existe otro perfil con ese nombre.");
            return Page();
        }

        profile.Name = name;
        profile.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();

        var seleccionadas = (Input.SelectedActionIds ?? []).ToHashSet();

        // Quitar las que ya no están marcadas.
        foreach (var pa in profile.ProfileActions.Where(pa => !seleccionadas.Contains(pa.ActionId)).ToList())
        {
            profile.ProfileActions.Remove(pa);
        }

        // Agregar las nuevas.
        var yaAsignadas = profile.ProfileActions.Select(pa => pa.ActionId).ToHashSet();
        foreach (var actionId in seleccionadas.Where(actionId => !yaAsignadas.Contains(actionId)))
        {
            profile.ProfileActions.Add(new ProfileAction { ProfileId = profile.Id, ActionId = actionId });
        }

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Profiles/Index");
    }

    public sealed class InputModel
    {
        public long Id { get; set; }

        [Required(ErrorMessage = "Ingresa el nombre del perfil.")]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Descripción")]
        public string? Description { get; set; }

        public List<long> SelectedActionIds { get; set; } = [];
    }
}
