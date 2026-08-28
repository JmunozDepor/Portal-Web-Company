using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.GruposMenu;

/// <summary>
/// Self-service: grupos de menú propios de la organización -- ver
/// IOrganizationMenuGroupService. Las plantillas globales de plataforma se muestran de
/// solo lectura (no se pueden editar/eliminar desde acá). Edición inline vía ?editId=,
/// mismo criterio que Modulo.ImportacionGenerica/Pages/CamposUsuario/Index.
/// </summary>
public sealed class IndexModel : AdminPageModelBase
{
    private readonly IOrganizationMenuGroupService _menuGroups;
    private readonly ITenantUserAdminService _tenantUsers;
    private readonly IOrganizationProfileService _profiles;

    public IndexModel(IOrganizationMenuGroupService menuGroups, ITenantUserAdminService tenantUsers, IOrganizationProfileService profiles, ICurrentUserContext currentUser) : base(currentUser)
    {
        _menuGroups = menuGroups;
        _tenantUsers = tenantUsers;
        _profiles = profiles;
    }

    [BindProperty(SupportsGet = true)]
    public long? EditId { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<OrganizationMenuGroupDto> Groups { get; private set; } = [];
    public List<SelectListItem> Companies { get; private set; } = [];
    public List<LeafMenuDto> LeafMenus { get; private set; } = [];
    public List<SelectListItem> ProfileOptions { get; private set; } = [];
    public bool IsEditingGlobalTemplate { get; private set; }

    public async Task OnGetAsync()
    {
        Groups = await _menuGroups.ListAsync();
        await LoadOptionsAsync();

        if (EditId is { } id)
        {
            var existing = await _menuGroups.GetAsync(id);
            if (existing is not null)
            {
                IsEditingGlobalTemplate = existing.IsGlobalTemplate;
                Input = new InputModel
                {
                    Name = existing.Name,
                    Description = existing.Description,
                    IsActive = existing.IsActive,
                    CompanyId = existing.CompanyId,
                    SelectedMenuIds = existing.MenuIds.ToList(),
                    DefaultProfileByMenu = existing.DefaultProfileByMenu.ToDictionary(kv => kv.Key, kv => kv.Value),
                };
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoadOptionsAsync();

        if (!ModelState.IsValid)
        {
            Groups = await _menuGroups.ListAsync();
            return Page();
        }

        try
        {
            if (EditId is { } id)
            {
                await _menuGroups.UpdateAsync(id, Input.Name, Input.Description, Input.IsActive, Input.CompanyId, Input.SelectedMenuIds ?? [], Input.DefaultProfileByMenu);
                MensajeExito = "Grupo de menú actualizado.";
            }
            else
            {
                await _menuGroups.CreateAsync(Input.Name, Input.Description, Input.CompanyId, Input.SelectedMenuIds ?? [], Input.DefaultProfileByMenu);
                MensajeExito = "Grupo de menú creado.";
            }
        }
        catch (Exception ex)
        {
            MensajeError = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        try
        {
            await _menuGroups.DeleteAsync(id);
            MensajeExito = "Grupo de menú eliminado.";
        }
        catch (Exception ex)
        {
            MensajeError = ex.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadOptionsAsync()
    {
        var companies = await _tenantUsers.ListCompaniesAsync();
        Companies = companies.Select(c => new SelectListItem($"{c.Code} — {c.Name}", c.Id.ToString())).ToList();
        LeafMenus = (await _tenantUsers.ListLeafMenusAsync()).ToList();

        var profiles = await _profiles.ListAsync();
        ProfileOptions = profiles.Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToList();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Ingresá el nombre del grupo.")]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Descripción")]
        public string? Description { get; set; }

        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Compañía (vacío = aplica a todas)")]
        public Guid? CompanyId { get; set; }

        public List<long> SelectedMenuIds { get; set; } = [];

        /// <summary>Perfil por defecto que hereda cualquier usuario de este grupo para ese nodo -- ver MenuGroupItem.DefaultProfileId.</summary>
        public Dictionary<long, long?> DefaultProfileByMenu { get; set; } = [];
    }
}
