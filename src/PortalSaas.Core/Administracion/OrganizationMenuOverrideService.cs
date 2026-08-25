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

    public OrganizationMenuOverrideService(PortalSaasDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<MenuOverrideRowDto>> ListAsync(CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var menus = await _db.Menus
            .Where(m => m.IsActive)
            .OrderBy(m => m.OriginModule).ThenBy(m => m.Level).ThenBy(m => m.Order)
            .ToListAsync(ct);

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

        var validMenuIds = (await _db.Menus.Where(m => m.IsActive).Select(m => m.Id).ToListAsync(ct)).ToHashSet();

        var existing = await _db.OrganizationMenuOverrides
            .Where(o => o.OrganizationId == organizationId)
            .ToDictionaryAsync(o => o.MenuId, ct);

        foreach (var (menuId, input) in overridesByMenuId)
        {
            if (!validMenuIds.Contains(menuId))
            {
                continue;
            }

            var customLabel = string.IsNullOrWhiteSpace(input.CustomLabel) ? null : input.CustomLabel;
            var esValorPorDefecto = customLabel is null && input.CustomOrder is null && !input.IsHidden;
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
                    IsHidden = input.IsHidden,
                });
            }
            else
            {
                actual.CustomLabel = customLabel;
                actual.CustomOrder = input.CustomOrder;
                actual.IsHidden = input.IsHidden;
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
