using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.MenuGroups;

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

    public List<Menu> AllMenus { get; private set; } = [];
    public List<SelectListItem> ProfileOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var menuGroup = await _db.MenuGroups
            .Include(g => g.MenuGroupItems)
            .FirstOrDefaultAsync(g => g.Id == id);
        if (menuGroup is null)
        {
            return NotFound();
        }

        await CargarMenusAsync();

        Input = new InputModel
        {
            Id = menuGroup.Id,
            Name = menuGroup.Name,
            Description = menuGroup.Description,
            IsActive = menuGroup.IsActive,
            SelectedMenuIds = menuGroup.MenuGroupItems.Select(i => i.MenuId).ToList(),
            DefaultProfileByMenu = menuGroup.MenuGroupItems.ToDictionary(i => i.MenuId, i => i.DefaultProfileId),
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var menuGroup = await _db.MenuGroups
            .Include(g => g.MenuGroupItems)
            .FirstOrDefaultAsync(g => g.Id == Input.Id);
        if (menuGroup is null)
        {
            return NotFound();
        }

        await CargarMenusAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var name = Input.Name.Trim();
        var nombreEnUso = await _db.MenuGroups.AnyAsync(g => g.Name == name && g.Id != Input.Id);
        if (nombreEnUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Name)}", "Ya existe otro grupo de menú con ese nombre.");
            return Page();
        }

        menuGroup.Name = name;
        menuGroup.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();
        menuGroup.IsActive = Input.IsActive;

        var seleccionados = (Input.SelectedMenuIds ?? []).ToHashSet();
        var perfilPorDefectoPorMenu = Input.DefaultProfileByMenu ?? [];

        foreach (var item in menuGroup.MenuGroupItems.Where(i => !seleccionados.Contains(i.MenuId)).ToList())
        {
            menuGroup.MenuGroupItems.Remove(item);
        }

        var yaAsignados = menuGroup.MenuGroupItems.ToDictionary(i => i.MenuId);
        foreach (var menuId in seleccionados)
        {
            var defaultProfileId = perfilPorDefectoPorMenu.GetValueOrDefault(menuId);
            var defaultProfileIdNormalizado = defaultProfileId is null or 0 ? null : defaultProfileId;

            if (yaAsignados.TryGetValue(menuId, out var existente))
            {
                // Ya estaba en el grupo -- solo actualiza el Profile por defecto (el
                // checkbox seguía tildado, esto no es un alta nueva).
                existente.DefaultProfileId = defaultProfileIdNormalizado;
            }
            else
            {
                menuGroup.MenuGroupItems.Add(new MenuGroupItem
                {
                    MenuGroupId = menuGroup.Id,
                    MenuId = menuId,
                    DefaultProfileId = defaultProfileIdNormalizado,
                });
            }
        }

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/MenuGroups/Index");
    }

    private async Task CargarMenusAsync()
    {
        AllMenus = await _db.Menus
            .OrderBy(m => m.OriginModule).ThenBy(m => m.Level).ThenBy(m => m.Order)
            .ToListAsync();

        ProfileOptions = await _db.Profiles
            .OrderBy(p => p.Name)
            .Select(p => new SelectListItem(p.Name, p.Id.ToString()))
            .ToListAsync();
    }

    public sealed class InputModel
    {
        public long Id { get; set; }

        [Required(ErrorMessage = "Ingresa el nombre del grupo.")]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Descripción")]
        public string? Description { get; set; }

        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;

        public List<long> SelectedMenuIds { get; set; } = [];

        /// <summary>Profile por defecto que hereda cualquier usuario de este grupo para ese nodo -- ver MenuGroupItem.DefaultProfileId.</summary>
        public Dictionary<long, long?> DefaultProfileByMenu { get; set; } = [];
    }
}
