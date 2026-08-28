using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Users;

/// <summary>
/// Asigna MenuGroups (navegación) y, por cada nodo de menú final, un Profile (acciones
/// permitidas) a un usuario -- SIEMPRE por compañía (UserMenuGroup/UserMenuProfile
/// llevan CompanyId, ver Entities/). Requiere que la organización ya tenga al menos
/// una Company (ver Organizations/Companies) -- sin eso no hay nada que seleccionar.
/// </summary>
[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class PermissionsModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public PermissionsModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public Organization Organization { get; private set; } = null!;
    public User TargetUser { get; private set; } = null!;
    public List<SelectListItem> Companies { get; private set; } = [];
    public bool TieneCompanies { get; private set; }
    public Guid SelectedCompanyId { get; private set; }

    public List<MenuGroup> AllMenuGroups { get; private set; } = [];
    public List<Menu> LeafMenus { get; private set; } = [];
    public List<SelectListItem> ProfileOptions { get; private set; } = [];

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid organizationId, Guid userId, Guid? companyId)
    {
        if (!await CargarContextoAsync(organizationId, userId))
        {
            return NotFound();
        }

        if (!TieneCompanies)
        {
            return Page();
        }

        var companias = await _db.Companies.Where(c => c.OrganizationId == organizationId).OrderBy(c => c.Code).ToListAsync();
        SelectedCompanyId = companyId is { } id && companias.Any(c => c.Id == id) ? id : companias[0].Id;

        await CargarAsignacionesAsync(userId);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid organizationId, Guid userId)
    {
        if (!await CargarContextoAsync(organizationId, userId))
        {
            return NotFound();
        }

        var companiaValida = await _db.Companies.AnyAsync(c => c.Id == Input.CompanyId && c.OrganizationId == organizationId);
        if (!companiaValida)
        {
            return NotFound();
        }

        SelectedCompanyId = Input.CompanyId;

        // --- MenuGroups ---
        var gruposActuales = await _db.UserMenuGroups
            .Where(g => g.UserId == userId && g.CompanyId == Input.CompanyId)
            .ToListAsync();

        var gruposSeleccionados = (Input.SelectedMenuGroupIds ?? []).ToHashSet();

        _db.UserMenuGroups.RemoveRange(gruposActuales.Where(g => !gruposSeleccionados.Contains(g.MenuGroupId)));

        var gruposYaAsignados = gruposActuales.Select(g => g.MenuGroupId).ToHashSet();
        foreach (var menuGroupId in gruposSeleccionados.Where(id => !gruposYaAsignados.Contains(id)))
        {
            _db.UserMenuGroups.Add(new UserMenuGroup { UserId = userId, CompanyId = Input.CompanyId, MenuGroupId = menuGroupId });
        }

        // --- Profile por nodo de menú final ---
        var perfilesActuales = await _db.UserMenuProfiles
            .Where(p => p.UserId == userId && p.CompanyId == Input.CompanyId)
            .ToListAsync();
        var perfilesActualesPorMenu = perfilesActuales.ToDictionary(p => p.MenuId);

        foreach (var (menuId, profileId) in Input.ProfileByMenu ?? [])
        {
            var existente = perfilesActualesPorMenu.GetValueOrDefault(menuId);

            if (profileId is null or 0)
            {
                if (existente is not null)
                {
                    _db.UserMenuProfiles.Remove(existente);
                }

                continue;
            }

            if (existente is null)
            {
                _db.UserMenuProfiles.Add(new UserMenuProfile
                {
                    UserId = userId,
                    CompanyId = Input.CompanyId,
                    MenuId = menuId,
                    ProfileId = profileId.Value,
                });
            }
            else if (existente.ProfileId != profileId.Value)
            {
                existente.ProfileId = profileId.Value;
            }
        }

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/Users/Permissions", new { organizationId, userId, companyId = Input.CompanyId });
    }

    private async Task<bool> CargarContextoAsync(Guid organizationId, Guid userId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.OrganizationId == organizationId);
        if (organization is null || user is null)
        {
            return false;
        }

        Organization = organization;
        TargetUser = user;

        Companies = await _db.Companies
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.Code)
            .Select(c => new SelectListItem($"{c.Code} — {c.Name}", c.Id.ToString()))
            .ToListAsync();
        TieneCompanies = Companies.Count > 0;

        return true;
    }

    private async Task CargarAsignacionesAsync(Guid userId)
    {
        AllMenuGroups = await _db.MenuGroups.OrderBy(g => g.Name).ToListAsync();
        LeafMenus = await _db.Menus
            .Where(m => m.PagePath != null)
            .OrderBy(m => m.OriginModule).ThenBy(m => m.Order)
            .ToListAsync();
        ProfileOptions = await _db.Profiles
            .OrderBy(p => p.Name)
            .Select(p => new SelectListItem(p.Name, p.Id.ToString()))
            .ToListAsync();

        var gruposAsignados = await _db.UserMenuGroups
            .Where(g => g.UserId == userId && g.CompanyId == SelectedCompanyId)
            .Select(g => g.MenuGroupId)
            .ToListAsync();

        var perfilesAsignados = await _db.UserMenuProfiles
            .Where(p => p.UserId == userId && p.CompanyId == SelectedCompanyId)
            .ToDictionaryAsync(p => p.MenuId, p => (long?)p.ProfileId);

        Input = new InputModel
        {
            CompanyId = SelectedCompanyId,
            SelectedMenuGroupIds = gruposAsignados,
            ProfileByMenu = perfilesAsignados,
        };
    }

    public sealed class InputModel
    {
        public Guid CompanyId { get; set; }
        public List<long> SelectedMenuGroupIds { get; set; } = [];
        public Dictionary<long, long?> ProfileByMenu { get; set; } = [];
    }
}
