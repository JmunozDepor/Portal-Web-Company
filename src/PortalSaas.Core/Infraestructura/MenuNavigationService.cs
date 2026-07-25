using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Implementación real de IMenuNavigationService -- ver su doc-comment para el criterio
/// de filtrado (admin bypass / MenuGroup por compañía / expansión de ancestros).
/// Reescrito contra PortalSaasDbContext (EF Core), no HANA -- el equivalente en
/// PortalSAP_v2 (MenuNavegacionService) consultaba HANA directo porque ahí PORTALWEB
/// vivía en HANA; acá el árbol núcleo ya está en la base propia de la plataforma.
///
/// Sin N+1: como máximo 2 consultas reales (menús activos + ids asignados vía un solo
/// join), sin importar cuántos grupos/nodos tenga el usuario -- la expansión de
/// ancestros y el armado del árbol corren enteramente en memoria sobre la lista ya
/// cargada, ver ExpandWithAncestors/MenuTreeHelper.BuildTree.
/// </summary>
public sealed class MenuNavigationService : IMenuNavigationService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public MenuNavigationService(PortalSaasDbContext db, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public async Task<IReadOnlyList<MenuNodeDto>> GetVisibleMenuAsync(CancellationToken ct = default)
    {
        // Única consulta real para el árbol -- se carga completo una sola vez y se
        // filtra/expande en memoria más abajo, nunca por nodo.
        var activeMenus = await _db.Menus
            .Where(m => m.IsActive)
            .Select(m => new MenuNodeDto
            {
                Id = m.Id,
                ParentMenuId = m.ParentMenuId,
                OriginModule = m.OriginModule,
                Code = m.Code,
                Name = m.Name,
                Icon = m.Icon,
                PagePath = m.PagePath,
                Order = m.Order,
            })
            .ToListAsync(ct);

        if (_currentUser.IsAdmin)
        {
            return MenuTreeHelper.BuildTree(activeMenus);
        }

        if (!_currentCompany.HasCompany)
        {
            return [];
        }

        var userId = _currentUser.UserId;
        var companyId = _currentCompany.CompanyId;

        // Segunda y última consulta real -- un solo join, independiente de cuántos
        // MenuGroups tenga el usuario o cuántos nodos tenga cada uno.
        var assignedMenuIds = await _db.UserMenuGroups
            .Where(ug => ug.UserId == userId && ug.CompanyId == companyId)
            .SelectMany(ug => ug.MenuGroup.MenuGroupItems.Select(item => item.MenuId))
            .Distinct()
            .ToListAsync(ct);

        var visibleIds = ExpandWithAncestors(activeMenus, assignedMenuIds);
        var visibleNodes = activeMenus.Where(m => visibleIds.Contains(m.Id)).ToList();

        return MenuTreeHelper.BuildTree(visibleNodes);
    }

    /// <summary>
    /// Sube por ParentMenuId agregando cada ancestro al set visible, hasta la raíz --
    /// así una carpeta contenedora aparece aunque no esté asignada ella misma. Todo en
    /// memoria sobre `allNodes` (ya cargado por el único SELECT de arriba), ninguna
    /// consulta adicional.
    /// </summary>
    private static HashSet<long> ExpandWithAncestors(List<MenuNodeDto> allNodes, List<long> assignedIds)
    {
        var byId = allNodes.ToDictionary(n => n.Id);
        var visible = new HashSet<long>(assignedIds);

        foreach (var id in assignedIds)
        {
            var current = byId.GetValueOrDefault(id);
            while (current?.ParentMenuId is { } parentId && visible.Add(parentId))
            {
                current = byId.GetValueOrDefault(parentId);
            }
        }

        return visible;
    }
}
