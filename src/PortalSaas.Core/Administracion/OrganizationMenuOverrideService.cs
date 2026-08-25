using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Administracion;

/// <summary>Implementación real de IOrganizationMenuOverrideService, acotada a ICurrentUserContext.OrganizationId.</summary>
public sealed class OrganizationMenuOverrideService : IOrganizationMenuOverrideService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IModuleAccessService _moduleAccess;

    public OrganizationMenuOverrideService(PortalSaasDbContext db, ICurrentUserContext currentUser, IModuleAccessService moduleAccess)
    {
        _db = db;
        _currentUser = currentUser;
        _moduleAccess = moduleAccess;
    }

    public async Task<IReadOnlyList<MenuOverrideRowDto>> ListAsync(CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var menus = await _db.Menus
            .Where(m => m.IsActive)
            .OrderBy(m => m.OriginModule).ThenBy(m => m.Level).ThenBy(m => m.Order)
            .ToListAsync(ct);

        // Mismo criterio de 2 pasos que MenuNavigationService.GetVisibleMenuAsync --
        // no listar acá nodos de módulos que la organización no contrató o que el
        // admin ya ocultó completos desde /organizacion/modulos.
        menus = await FiltrarPorAccesoDeModuloAsync(menus, organizationId, ct);

        var overrides = await _db.OrganizationMenuOverrides
            .Where(o => o.OrganizationId == organizationId)
            .ToDictionaryAsync(o => o.MenuId, ct);

        return menus
            .Select(m =>
            {
                var ov = overrides.GetValueOrDefault(m.Id);
                return new MenuOverrideRowDto
                {
                    MenuId = m.Id,
                    Level = m.Level,
                    OriginModule = m.OriginModule,
                    Code = m.Code,
                    Name = m.Name,
                    CustomLabel = ov?.CustomLabel,
                    CustomOrder = ov?.CustomOrder,
                    IsHidden = ov?.IsHidden ?? false,
                };
            })
            .ToList();
    }

    public async Task SaveOverridesAsync(Dictionary<long, MenuOverrideInput> overridesByMenuId, CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var menus = await _db.Menus.Where(m => m.IsActive).ToListAsync(ct);
        menus = await FiltrarPorAccesoDeModuloAsync(menus, organizationId, ct);
        var originModuleByMenuId = menus.ToDictionary(m => m.Id, m => m.OriginModule);
        var validMenuIds = originModuleByMenuId.Keys.ToHashSet();

        var existing = await _db.OrganizationMenuOverrides
            .Where(o => o.OrganizationId == organizationId)
            .ToDictionaryAsync(o => o.MenuId, ct);

        foreach (var (menuId, input) in overridesByMenuId)
        {
            if (!validMenuIds.Contains(menuId))
            {
                continue;
            }

            // El nodo raíz de Administración nunca puede quedar oculto por override de
            // organización -- si se ocultara, desaparecería del sidebar la pantalla que
            // permite revertirlo (/organizacion/menus), dejando a los admins sin forma
            // visual de deshacer el error (auto-lockout). Se ignora el IsHidden que vino
            // del input y se fuerza a false, sea cual sea el origen del POST.
            var isHidden = originModuleByMenuId[menuId] == "Administracion" ? false : input.IsHidden;

            var customLabel = string.IsNullOrWhiteSpace(input.CustomLabel) ? null : input.CustomLabel;
            var esValorPorDefecto = customLabel is null && input.CustomOrder is null && !isHidden;
            var actual = existing.GetValueOrDefault(menuId);

            if (esValorPorDefecto)
            {
                if (actual is not null)
                {
                    _db.OrganizationMenuOverrides.Remove(actual);
                }

                continue;
            }

            if (actual is null)
            {
                _db.OrganizationMenuOverrides.Add(new OrganizationMenuOverride
                {
                    OrganizationId = organizationId,
                    MenuId = menuId,
                    CustomLabel = customLabel,
                    CustomOrder = input.CustomOrder,
                    IsHidden = isHidden,
                });
            }
            else
            {
                actual.CustomLabel = customLabel;
                actual.CustomOrder = input.CustomOrder;
                actual.IsHidden = isHidden;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Mismo criterio de 2 pasos que MenuNavigationService.GetVisibleMenuAsync: primero
    /// catalogados/contratados, después ocultos explícitos del admin -- ver el
    /// doc-comment de ese método para el porqué del orden y de por qué corre para
    /// admins también (preferencia de organización, no permiso individual).
    /// </summary>
    private async Task<List<Menu>> FiltrarPorAccesoDeModuloAsync(List<Menu> menus, Guid organizationId, CancellationToken ct)
    {
        var catalogedModules = await _moduleAccess.GetCatalogedModuleCodesAsync(ct);
        if (catalogedModules.Count > 0)
        {
            var contractedModules = await _moduleAccess.GetContractedModuleCodesAsync(organizationId, ct);
            menus = menus
                .Where(m => !catalogedModules.Contains(m.OriginModule) || contractedModules.Contains(m.OriginModule))
                .ToList();
        }

        var hiddenModules = await _moduleAccess.GetHiddenModuleCodesAsync(organizationId, ct);
        if (hiddenModules.Count > 0)
        {
            menus = menus.Where(m => !hiddenModules.Contains(m.OriginModule)).ToList();
        }

        return menus;
    }
}
