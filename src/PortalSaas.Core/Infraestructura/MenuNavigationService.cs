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
/// Sin N+1: como máximo 3 consultas reales (menús activos, ids heredados de grupo, ids
/// con asignación manual), sin importar cuántos grupos/nodos/perfiles tenga el usuario
/// -- la expansión de ancestros y el armado del árbol corren enteramente en memoria
/// sobre la lista ya cargada, ver ExpandWithAncestors/MenuTreeHelper.BuildTree.
///
/// Visibilidad = UNIÓN de "heredado del grupo" (MenuGroupItem.DefaultProfileId) y
/// "asignación manual" (UserMenuProfile) -- corregido 27 jul 2026 (bug real reportado:
/// un usuario con SOLO un MenuGroup asignado, sin ninguna fila individual en
/// UserMenuProfile, no veía nada de ese grupo porque antes exigía la intersección de
/// las dos cosas). La autorización real (qué ACCIÓN puede hacer, no solo si el nodo
/// aparece) vive en CurrentUserContext.HasActionAsync, con la misma resolución
/// override-o-herencia -- ver el doc-comment de MenuGroupItem.DefaultProfileId.
/// </summary>
public sealed class MenuNavigationService : IMenuNavigationService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly IModuleAccessService _moduleAccess;

    public MenuNavigationService(PortalSaasDbContext db, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany, IModuleAccessService moduleAccess)
    {
        _db = db;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
        _moduleAccess = moduleAccess;
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

        // Filtro por módulos contratados ANTES del bypass de administrador -- es un
        // gate comercial (qué pagó la organización), no de autorización (quién puede
        // ver qué dentro de lo que sí pagó), así que un admin de la organización
        // tampoco debería ver un módulo que la organización no tiene contratado. Con
        // platform_modules vacío (sin catálogo comercial cargado todavía, ver
        // CLAUDE.md) esto no filtra nada -- ver ModuleAccessService.
        var catalogedModules = await _moduleAccess.GetCatalogedModuleCodesAsync(ct);
        if (catalogedModules.Count > 0)
        {
            var contractedModules = await _moduleAccess.GetContractedModuleCodesAsync(_currentUser.OrganizationId, ct);
            activeMenus = activeMenus
                .Where(m => !catalogedModules.Contains(m.OriginModule) || contractedModules.Contains(m.OriginModule))
                .ToList();
        }

        // Oculto explícito del admin de organización (OrganizationModuleVisibility) --
        // aplica ANTES del bypass de administrador, igual que el filtro de contratados
        // de arriba: es una preferencia de la organización sobre qué se ve, no un
        // permiso individual, así que también alcanza a los admins de esa organización.
        var hiddenModules = await _moduleAccess.GetHiddenModuleCodesAsync(_currentUser.OrganizationId, ct);
        if (hiddenModules.Count > 0)
        {
            activeMenus = activeMenus.Where(m => !hiddenModules.Contains(m.OriginModule)).ToList();
        }

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

        // Segunda consulta real -- nodos que el usuario ve por HERENCIA de grupo: un
        // MenuGroup lo incluye Y ese ítem de grupo tiene un DefaultProfileId (ver
        // MenuGroupItem.DefaultProfileId) -- un grupo puede incluir un nodo solo para
        // navegación sin otorgar ningún permiso por sí solo, ese caso no cuenta acá.
        var inheritedMenuIds = await _db.UserMenuGroups
            .Where(ug => ug.UserId == userId && ug.CompanyId == companyId)
            .SelectMany(ug => ug.MenuGroup.MenuGroupItems)
            .Where(item => item.DefaultProfileId != null)
            .Select(item => item.MenuId)
            .Distinct()
            .ToListAsync(ct);

        // Tercera consulta real -- asignaciones MANUALES (UserMenuProfile), que
        // siempre ganan por sobre lo heredado del grupo para ese nodo puntual (ver
        // CurrentUserContext.HasActionAsync, misma resolución) pero para VISIBILIDAD
        // alcanza con que exista, venga de donde venga -- unión, no intersección
        // (antes de esta corrección exigía las dos cosas a la vez, así que un usuario
        // con SOLO un MenuGroup asignado -- sin fila individual en UserMenuProfile --
        // no veía nada del grupo, bug real reportado).
        var overriddenMenuIds = await _db.UserMenuProfiles
            .Where(up => up.UserId == userId && up.CompanyId == companyId)
            .Select(up => up.MenuId)
            .Distinct()
            .ToListAsync(ct);

        var visibleLeafIds = inheritedMenuIds.Union(overriddenMenuIds).ToList();

        var visibleIds = ExpandWithAncestors(activeMenus, visibleLeafIds);
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
