using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Host.ViewComponents;

/// <summary>
/// Renderiza el árbol de navegación del sidebar (Pages/Shared/_Layout.cshtml) --
/// invocado vía @await Component.InvokeAsync("SidebarMenu"). Ver
/// IMenuNavigationService para el criterio de filtrado (admin bypass / MenuGroup por
/// compañía / expansión de ancestros); la vista (Default.cshtml) solo recorre el árbol
/// ya armado, sin lógica de acceso a datos.
/// </summary>
public sealed class SidebarMenuViewComponent : ViewComponent
{
    private readonly IMenuNavigationService _menuNavigation;

    public SidebarMenuViewComponent(IMenuNavigationService menuNavigation)
    {
        _menuNavigation = menuNavigation;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var menu = await _menuNavigation.GetVisibleMenuAsync();
        return View(menu);
    }
}
