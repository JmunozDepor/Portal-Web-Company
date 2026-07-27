using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Administracion;

/// <summary>Implementación real de IOrganizationModuleVisibilityService, acotada a ICurrentUserContext.OrganizationId.</summary>
public sealed class OrganizationModuleVisibilityService : IOrganizationModuleVisibilityService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IModuleAccessService _moduleAccess;

    public OrganizationModuleVisibilityService(PortalSaasDbContext db, ICurrentUserContext currentUser, IModuleAccessService moduleAccess)
    {
        _db = db;
        _currentUser = currentUser;
        _moduleAccess = moduleAccess;
    }

    public async Task<IReadOnlyList<ModuleVisibilityDto>> ListAsync(CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var contractedCodes = await _moduleAccess.GetContractedModuleCodesAsync(organizationId, ct);
        if (contractedCodes.Count == 0)
        {
            return [];
        }

        var modules = await _db.PlatformModules
            .Where(m => contractedCodes.Contains(m.Code))
            .OrderBy(m => m.Name)
            .ToListAsync(ct);

        var hidden = await _db.OrganizationModuleVisibilities
            .Where(v => v.OrganizationId == organizationId)
            .ToDictionaryAsync(v => v.ModuleId, v => v.IsHidden, ct);

        return modules
            .Select(m => new ModuleVisibilityDto(m.Id, m.Code, m.Name, m.IsCore, hidden.GetValueOrDefault(m.Id)))
            .ToList();
    }

    public async Task SetHiddenAsync(long moduleId, bool isHidden, CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var existing = await _db.OrganizationModuleVisibilities
            .FirstOrDefaultAsync(v => v.OrganizationId == organizationId && v.ModuleId == moduleId, ct);

        if (existing is null)
        {
            _db.OrganizationModuleVisibilities.Add(new OrganizationModuleVisibility
            {
                OrganizationId = organizationId,
                ModuleId = moduleId,
                IsHidden = isHidden,
            });
        }
        else
        {
            existing.IsHidden = isHidden;
        }

        await _db.SaveChangesAsync(ct);
    }
}
