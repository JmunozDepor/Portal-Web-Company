using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Administracion;

/// <summary>Implementación real de IOrganizationProfileService, acotada a ICurrentUserContext.OrganizationId.</summary>
public sealed class OrganizationProfileService : IOrganizationProfileService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public OrganizationProfileService(PortalSaasDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<OrganizationProfileDto>> ListAsync(CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var profiles = await _db.Profiles
            .Include(p => p.ProfileActions)
            .Where(p => p.OrganizationId == null || p.OrganizationId == organizationId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        return profiles.Select(Map).ToList();
    }

    public async Task<OrganizationProfileDto?> GetAsync(long id, CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var profile = await _db.Profiles
            .Include(p => p.ProfileActions)
            .FirstOrDefaultAsync(p => p.Id == id && (p.OrganizationId == null || p.OrganizationId == organizationId), ct);

        return profile is null ? null : Map(profile);
    }

    public async Task<long> CreateAsync(string name, string? description, IReadOnlyList<long> actionIds, CancellationToken ct = default)
    {
        var entity = new Profile
        {
            OrganizationId = _currentUser.OrganizationId,
            Name = name,
            Description = description,
            ProfileActions = actionIds.Select(actionId => new ProfileAction { ActionId = actionId }).ToList(),
        };

        _db.Profiles.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task UpdateAsync(long id, string name, string? description, IReadOnlyList<long> actionIds, CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var entity = await _db.Profiles
            .Include(p => p.ProfileActions)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new InvalidOperationException("Perfil no encontrado.");

        if (entity.OrganizationId != organizationId)
        {
            throw new InvalidOperationException("No podés editar un perfil que no sea de tu organización (las plantillas de plataforma son de solo lectura).");
        }

        entity.Name = name;
        entity.Description = description;

        var selected = actionIds.ToHashSet();
        foreach (var pa in entity.ProfileActions.Where(pa => !selected.Contains(pa.ActionId)).ToList())
        {
            entity.ProfileActions.Remove(pa);
        }

        var already = entity.ProfileActions.Select(pa => pa.ActionId).ToHashSet();
        foreach (var actionId in selected.Where(actionId => !already.Contains(actionId)))
        {
            entity.ProfileActions.Add(new ProfileAction { ProfileId = entity.Id, ActionId = actionId });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var entity = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null)
        {
            return;
        }

        if (entity.OrganizationId != organizationId)
        {
            throw new InvalidOperationException("No podés eliminar un perfil que no sea de tu organización.");
        }

        _db.Profiles.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PermissionActionDto>> ListAllActionsAsync(CancellationToken ct = default)
    {
        return await _db.Actions
            .OrderBy(a => a.Id)
            .Select(a => new PermissionActionDto(a.Id, a.Code, a.Name))
            .ToListAsync(ct);
    }

    private static OrganizationProfileDto Map(Profile profile) => new(
        profile.Id,
        profile.Name,
        profile.Description,
        profile.OrganizationId is null,
        profile.ProfileActions.Select(pa => pa.ActionId).ToList());
}
