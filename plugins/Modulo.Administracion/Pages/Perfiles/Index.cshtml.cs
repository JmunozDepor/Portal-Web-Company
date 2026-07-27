using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.Perfiles;

/// <summary>
/// Self-service: perfiles propios de la organización -- ver IOrganizationProfileService.
/// Mismo patrón que Pages/GruposMenu/Index (edición inline vía ?editId=, plantillas
/// globales de solo lectura).
/// </summary>
public sealed class IndexModel : AdminPageModelBase
{
    private readonly IOrganizationProfileService _profiles;

    public IndexModel(IOrganizationProfileService profiles, ICurrentUserContext currentUser) : base(currentUser)
    {
        _profiles = profiles;
    }

    [BindProperty(SupportsGet = true)]
    public long? EditId { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<OrganizationProfileDto> Profiles { get; private set; } = [];
    public List<PermissionActionDto> AllActions { get; private set; } = [];
    public bool IsEditingGlobalTemplate { get; private set; }

    public async Task OnGetAsync()
    {
        Profiles = await _profiles.ListAsync();
        AllActions = (await _profiles.ListAllActionsAsync()).ToList();

        if (EditId is { } id)
        {
            var existing = await _profiles.GetAsync(id);
            if (existing is not null)
            {
                IsEditingGlobalTemplate = existing.IsGlobalTemplate;
                Input = new InputModel
                {
                    Name = existing.Name,
                    Description = existing.Description,
                    SelectedActionIds = existing.ActionIds.ToList(),
                };
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        AllActions = (await _profiles.ListAllActionsAsync()).ToList();

        if (!ModelState.IsValid)
        {
            Profiles = await _profiles.ListAsync();
            return Page();
        }

        try
        {
            if (EditId is { } id)
            {
                await _profiles.UpdateAsync(id, Input.Name, Input.Description, Input.SelectedActionIds ?? []);
                MensajeExito = "Perfil actualizado.";
            }
            else
            {
                await _profiles.CreateAsync(Input.Name, Input.Description, Input.SelectedActionIds ?? []);
                MensajeExito = "Perfil creado.";
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
            await _profiles.DeleteAsync(id);
            MensajeExito = "Perfil eliminado.";
        }
        catch (Exception ex)
        {
            MensajeError = ex.Message;
        }

        return RedirectToPage();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Ingresá el nombre del perfil.")]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Descripción")]
        public string? Description { get; set; }

        public List<long> SelectedActionIds { get; set; } = [];
    }
}
