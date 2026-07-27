using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Host.Pages.Home;

public sealed record HomeShortcutDto(long MenuId, string Name, string? Icon, string PagePath, int ColorIndex);

[Authorize]
public class IndexModel : PageModel
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IMenuNavigationService _menuNavigation;
    private readonly IUserHomeShortcutService _shortcuts;

    public IndexModel(ICurrentUserContext currentUser, IMenuNavigationService menuNavigation, IUserHomeShortcutService shortcuts)
    {
        _currentUser = currentUser;
        _menuNavigation = menuNavigation;
        _shortcuts = shortcuts;
    }

    public string? Email { get; private set; }
    public string? OrganizationSlug { get; private set; }

    /// <summary>True si el usuario ya eligió al menos un acceso puntual -- en ese caso
    /// Shortcuts son SUS páginas elegidas (con botón de quitar), no la grilla autogenerada
    /// por categoría. Las dos vistas no se mezclan: personalizar reemplaza el fallback
    /// por completo, no lo complementa (mismo criterio que PortalSAP_v2).</summary>
    public bool IsCustomized { get; private set; }

    public IReadOnlyList<HomeShortcutDto> Shortcuts { get; private set; } = [];

    /// <summary>Páginas hoja visibles para el usuario que todavía no eligió -- candidatas del picker "+".</summary>
    public IReadOnlyList<MenuNodeDto> AvailablePages { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Email = User.FindFirstValue(ClaimTypes.Email);
        OrganizationSlug = User.FindFirstValue("OrganizationSlug");

        var tree = await _menuNavigation.GetVisibleMenuAsync(ct);
        var allNodes = Flatten(tree);
        var byId = allNodes.ToDictionary(n => n.Id);

        var chosenIds = await _shortcuts.ListMenuIdsAsync(_currentUser.UserId, ct);

        if (chosenIds.Count > 0)
        {
            IsCustomized = true;
            Shortcuts = ResolveCustomShortcuts(tree, byId, chosenIds);
        }
        else
        {
            Shortcuts = ResolveShortcutsByCategory(tree);
        }

        var chosenSet = chosenIds.ToHashSet();
        AvailablePages = allNodes
            .Where(n => n.PagePath is not null && !chosenSet.Contains(n.Id))
            .OrderBy(n => n.Name)
            .ToList();
    }

    public async Task<IActionResult> OnPostAddShortcutAsync(long menuId, CancellationToken ct)
    {
        await _shortcuts.AddAsync(_currentUser.UserId, menuId, ct);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveShortcutAsync(long menuId, CancellationToken ct)
    {
        await _shortcuts.RemoveAsync(_currentUser.UserId, menuId, ct);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Account/Login");
    }

    private static List<MenuNodeDto> Flatten(IReadOnlyList<MenuNodeDto> roots)
    {
        var result = new List<MenuNodeDto>();
        void Visit(MenuNodeDto node)
        {
            result.Add(node);
            foreach (var child in node.Children)
            {
                Visit(child);
            }
        }
        foreach (var root in roots)
        {
            Visit(root);
        }
        return result;
    }

    // Elecciones puntuales del usuario, en el orden en que las agregó. Un id que ya no
    // está en el árbol visible (permisos cambiaron, página desactivada) se omite en
    // silencio.
    //
    // Icono/color NO salen del propio nodo -- una página hoja casi nunca tiene ícono
    // propio (el sidebar la deja con un punto genérico a propósito, para no competir con
    // el ícono de la carpeta). El ícono se hereda del ANCESTRO MÁS CERCANO que tenga uno
    // propio. El color de categoría, en cambio, siempre sale de la raíz (mismo orden que
    // usa el sidebar, ver SidebarMenu/Default.cshtml), para que la tarjeta y la categoría
    // del menú se lean como lo mismo.
    private static List<HomeShortcutDto> ResolveCustomShortcuts(
        IReadOnlyList<MenuNodeDto> roots, Dictionary<long, MenuNodeDto> byId, IReadOnlyList<long> chosenIds)
    {
        var colorIndexByRoot = ResolveColorIndexByRoot(roots);
        var result = new List<HomeShortcutDto>();

        foreach (var id in chosenIds)
        {
            if (!byId.TryGetValue(id, out var node) || node.PagePath is null)
            {
                continue;
            }

            var icon = ResolveInheritedIcon(node, byId);
            var root = ResolveRoot(node, byId);
            var colorIndex = root is not null ? colorIndexByRoot.GetValueOrDefault(root.Id, 1) : 1;
            result.Add(new HomeShortcutDto(node.Id, node.Name, icon, node.PagePath, colorIndex));
        }

        return result;
    }

    // Sube de a un nivel (padre, abuelo, ...) y usa el primero que tenga ícono propio.
    private static string? ResolveInheritedIcon(MenuNodeDto node, Dictionary<long, MenuNodeDto> byId)
    {
        if (node.Icon is not null)
        {
            return node.Icon;
        }

        var parentId = node.ParentMenuId;
        while (parentId is { } id && byId.TryGetValue(id, out var parent))
        {
            if (parent.Icon is not null)
            {
                return parent.Icon;
            }

            parentId = parent.ParentMenuId;
        }

        return null;
    }

    // Fallback cuando el usuario todavía no personalizó nada: una tarjeta por categoría
    // raíz visible. Si la categoría es una carpeta, la tarjeta apunta a la primera página
    // hoja dentro de su subárbol (DFS).
    private static List<HomeShortcutDto> ResolveShortcutsByCategory(IReadOnlyList<MenuNodeDto> roots)
    {
        var colorIndexByRoot = ResolveColorIndexByRoot(roots);
        var result = new List<HomeShortcutDto>();

        foreach (var root in roots)
        {
            var pagePath = root.PagePath ?? FindFirstDescendantPage(root);
            if (pagePath is null)
            {
                continue;
            }

            result.Add(new HomeShortcutDto(root.Id, root.Name, root.Icon, pagePath, colorIndexByRoot.GetValueOrDefault(root.Id, 1)));
        }

        return result;
    }

    private static string? FindFirstDescendantPage(MenuNodeDto node)
    {
        foreach (var child in node.Children)
        {
            if (child.PagePath is not null)
            {
                return child.PagePath;
            }

            var fromGrandchild = FindFirstDescendantPage(child);
            if (fromGrandchild is not null)
            {
                return fromGrandchild;
            }
        }

        return null;
    }

    // Sube por ParentMenuId hasta el ancestro raíz -- es la fuente del ícono/color "de
    // categoría" para cualquier descendiente, sin importar la profundidad real.
    private static MenuNodeDto? ResolveRoot(MenuNodeDto node, Dictionary<long, MenuNodeDto> byId)
    {
        var current = node;
        while (current.ParentMenuId is { } parentId && byId.TryGetValue(parentId, out var parent))
        {
            current = parent;
        }

        return current.ParentMenuId is null ? current : null;
    }

    // Misma paleta cíclica --menu-color-1..6 (site.css) y mismo orden que usa el sidebar
    // (SidebarMenu/Default.cshtml) para asignar color a cada categoría raíz -- una fuente
    // única evita que Inicio y el sidebar terminen mostrando colores distintos para la
    // misma categoría.
    private static Dictionary<long, int> ResolveColorIndexByRoot(IReadOnlyList<MenuNodeDto> roots)
    {
        // Mismo criterio exacto que SidebarMenu/Default.cshtml: solo las carpetas raíz
        // (PagePath null) consumen un color de la paleta -- una raíz que fuera hoja
        // suelta no se colorea ahí, así que tampoco acá.
        var result = new Dictionary<long, int>();
        var colorIndex = 0;
        foreach (var root in roots)
        {
            if (root.PagePath is null)
            {
                colorIndex++;
                result[root.Id] = ((colorIndex - 1) % 6) + 1;
            }
        }
        return result;
    }
}
