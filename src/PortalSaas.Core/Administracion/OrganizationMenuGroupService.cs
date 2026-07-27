using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Administracion;

/// <summary>Implementación real de IOrganizationMenuGroupService, acotada a ICurrentUserContext.OrganizationId.</summary>
public sealed class OrganizationMenuGroupService : IOrganizationMenuGroupService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public OrganizationMenuGroupService(PortalSaasDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<OrganizationMenuGroupDto>> ListAsync(CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var groups = await _db.MenuGroups
            .Include(g => g.MenuGroupItems)
            .Where(g => g.OrganizationId == null || g.OrganizationId == organizationId)
            .OrderBy(g => g.Name)
            .ToListAsync(ct);

        return groups.Select(Map).ToList();
    }

    public async Task<OrganizationMenuGroupDto?> GetAsync(long id, CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var group = await _db.MenuGroups
            .Include(g => g.MenuGroupItems)
            .FirstOrDefaultAsync(g => g.Id == id && (g.OrganizationId == null || g.OrganizationId == organizationId), ct);

        return group is null ? null : Map(group);
    }

    public async Task<long> CreateAsync(string name, string? description, Guid? companyId, IReadOnlyList<long> menuIds, IReadOnlyDictionary<long, long?>? defaultProfileByMenu = null, CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;
        var perfilPorMenu = defaultProfileByMenu ?? new Dictionary<long, long?>();
        await ValidarPerfilesAsync(perfilPorMenu, organizationId, ct);

        var entity = new MenuGroup
        {
            OrganizationId = organizationId,
            CompanyId = companyId,
            Name = name,
            Description = description,
            IsActive = true,
            MenuGroupItems = menuIds
                .Select(menuId => new MenuGroupItem { MenuId = menuId, DefaultProfileId = perfilPorMenu.GetValueOrDefault(menuId) })
                .ToList(),
        };

        _db.MenuGroups.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task UpdateAsync(long id, string name, string? description, bool isActive, Guid? companyId, IReadOnlyList<long> menuIds, IReadOnlyDictionary<long, long?>? defaultProfileByMenu = null, CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;
        var perfilPorMenu = defaultProfileByMenu ?? new Dictionary<long, long?>();
        await ValidarPerfilesAsync(perfilPorMenu, organizationId, ct);

        var entity = await _db.MenuGroups
            .Include(g => g.MenuGroupItems)
            .FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new InvalidOperationException("Grupo de menú no encontrado.");

        if (entity.OrganizationId != organizationId)
        {
            throw new InvalidOperationException("No podés editar un grupo de menú que no sea de tu organización (las plantillas de plataforma son de solo lectura).");
        }

        entity.Name = name;
        entity.Description = description;
        entity.IsActive = isActive;
        entity.CompanyId = companyId;

        var selected = menuIds.ToHashSet();
        foreach (var item in entity.MenuGroupItems.Where(i => !selected.Contains(i.MenuId)).ToList())
        {
            entity.MenuGroupItems.Remove(item);
        }

        var yaAsignados = entity.MenuGroupItems.ToDictionary(i => i.MenuId);
        foreach (var menuId in selected)
        {
            var defaultProfileId = perfilPorMenu.GetValueOrDefault(menuId);
            if (yaAsignados.TryGetValue(menuId, out var existente))
            {
                existente.DefaultProfileId = defaultProfileId;
            }
            else
            {
                entity.MenuGroupItems.Add(new MenuGroupItem { MenuGroupId = entity.Id, MenuId = menuId, DefaultProfileId = defaultProfileId });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Un Perfil por defecto debe ser una plantilla global o pertenecer a la organización actual -- nunca de otra organización.</summary>
    private async Task ValidarPerfilesAsync(IReadOnlyDictionary<long, long?> defaultProfileByMenu, Guid? organizationId, CancellationToken ct)
    {
        var profileIds = defaultProfileByMenu.Values.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        if (profileIds.Count == 0)
        {
            return;
        }

        var validos = await _db.Profiles
            .Where(p => profileIds.Contains(p.Id) && (p.OrganizationId == null || p.OrganizationId == organizationId))
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (validos.Count != profileIds.Count)
        {
            throw new InvalidOperationException("Uno de los perfiles seleccionados no existe o no pertenece a tu organización.");
        }
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var entity = await _db.MenuGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (entity is null)
        {
            return;
        }

        if (entity.OrganizationId != organizationId)
        {
            throw new InvalidOperationException("No podés eliminar un grupo de menú que no sea de tu organización.");
        }

        _db.MenuGroups.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    private static OrganizationMenuGroupDto Map(MenuGroup group) => new(
        group.Id,
        group.Name,
        group.Description,
        group.IsActive,
        group.CompanyId,
        group.OrganizationId is null,
        group.MenuGroupItems.Select(i => i.MenuId).ToList(),
        group.MenuGroupItems.ToDictionary(i => i.MenuId, i => i.DefaultProfileId));
}
