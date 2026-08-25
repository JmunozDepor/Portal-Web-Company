using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.Menus;

/// <summary>
/// Self-service: reordenar, ocultar y renombrar nodos existentes del árbol de menú
/// para la organización actual -- ver IOrganizationMenuOverrideService. Nunca crea
/// nodos nuevos, solo personaliza los que ya sincronizaron los plugins.
/// </summary>
public sealed class IndexModel : AdminPageModelBase
{
    private readonly IOrganizationMenuOverrideService _overrides;

    public IndexModel(IOrganizationMenuOverrideService overrides, ICurrentUserContext currentUser) : base(currentUser)
    {
        _overrides = overrides;
    }

    public IReadOnlyList<MenuOverrideRowDto> Rows { get; private set; } = [];

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        Rows = await _overrides.ListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var hiddenSet = (Input.HiddenMenuIds ?? []).ToHashSet();
        var todosLosIds = Input.CustomLabel.Keys.Union(Input.CustomOrder.Keys).Union(hiddenSet);

        var overridesByMenuId = todosLosIds.ToDictionary(
            menuId => menuId,
            menuId => new MenuOverrideInput(
                Input.CustomLabel.GetValueOrDefault(menuId),
                Input.CustomOrder.GetValueOrDefault(menuId),
                hiddenSet.Contains(menuId)));

        try
        {
            await _overrides.SaveOverridesAsync(overridesByMenuId);
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
            Rows = await _overrides.ListAsync();
            return Page();
        }

        MensajeExito = "Menús actualizados correctamente.";
        return RedirectToPage();
    }

    public sealed class InputModel
    {
        public Dictionary<long, string?> CustomLabel { get; set; } = [];
        public Dictionary<long, int?> CustomOrder { get; set; } = [];
        public List<long> HiddenMenuIds { get; set; } = [];
    }
}
