using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.Modulos;

/// <summary>
/// Self-service: qué módulos contratados por la organización quedan ocultos del
/// árbol de menú -- ver IOrganizationModuleVisibilityService. Nunca cambia lo
/// contratado, solo lo que se ve.
/// </summary>
public sealed class IndexModel : AdminPageModelBase
{
    private readonly IOrganizationModuleVisibilityService _visibility;

    public IndexModel(IOrganizationModuleVisibilityService visibility, ICurrentUserContext currentUser) : base(currentUser)
    {
        _visibility = visibility;
    }

    public IReadOnlyList<ModuleVisibilityDto> Modules { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Modules = await _visibility.ListAsync();
    }

    public async Task<IActionResult> OnPostToggleAsync(long moduleId, bool isHidden)
    {
        await _visibility.SetHiddenAsync(moduleId, isHidden);
        MensajeExito = "Visibilidad actualizada.";
        return RedirectToPage();
    }
}
